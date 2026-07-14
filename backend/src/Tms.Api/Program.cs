using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tms.Api.Data;
using Tms.Api.Middleware;
using Tms.Api.Models;
using Tms.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Neon Postgres connection string comes from configuration / env var
// ConnectionStrings__TmsDb (set in Azure App Service configuration, never committed).
var connectionString = builder.Configuration.GetConnectionString("TmsDb")
    ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__TMSDB")
    ?? throw new InvalidOperationException("Missing TmsDb connection string.");

var signingKey = builder.Configuration["Auth:SigningKey"]
    ?? throw new InvalidOperationException(
        "Missing Auth:SigningKey. Set it via user-secrets locally or App Service configuration in prod - never commit it.");

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IPasswordHasher<PlatformUser>, PasswordHasher<PlatformUser>>();
builder.Services.AddScoped<IPasswordHasher<PortalCustomer>, PasswordHasher<PortalCustomer>>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IRuleEngineService, RuleEngineService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Module 11 - Integrations & Public API. Typed HttpClient via
// AddHttpClient so outbound webhook deliveries reuse pooled connections
// rather than a raw `new HttpClient()` (socket-exhaustion pitfall). A
// generous-but-bounded timeout here is a backstop only - WebhookService
// applies its own tighter 5s-per-delivery timeout so one slow subscriber
// can't stall a ticket create/update indefinitely.
builder.Services.AddHttpClient<IWebhookService, WebhookService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Module 1 - Authentication & Identity (SSO). Discovery document, token
// endpoint, and JWKS fetches all happen synchronously inline in the OIDC
// login flow (SsoAuthController.Start/OidcCallback) - a slow/unreachable IdP
// adds directly to that request's latency, same tradeoff already accepted
// for outbound webhooks above. A tighter timeout than webhooks' 10s since
// this sits in the middle of an interactive browser redirect a user is
// actively waiting on, not a fire-and-forget background notification.
builder.Services.AddHttpClient<IOidcService, OidcService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});

// Module 5.2 - Plans & Billing Administration. Scoped (not singleton) since
// it reads IConfiguration per-call rather than caching the secret key at
// construction - matches the read-lazily-not-at-startup reasoning on
// StripeService itself.
builder.Services.AddScoped<IStripeService, StripeService>();

builder.Services.AddDbContext<TmsDbContext>(options =>
    options.UseNpgsql(connectionString));

// Serialize enums (TenantStatus, TicketStatus, TicketPriority, Role, PlatformRole)
// as their string names in JSON, matching how they're stored in Postgres -
// otherwise clients see raw numbers like status:0 instead of "Trial".
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidAudience = builder.Configuration["Auth:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    // Module 11 - Integrations & Public API. A second, independent
    // authentication scheme alongside the default JwtBearer one above -
    // external systems authenticate with an X-Api-Key header instead of a
    // staff/portal JWT (see ApiKeyAuthenticationHandler). Registered here
    // but never made the default, so every existing [Authorize] on staff/
    // portal controllers keeps resolving against JwtBearer exactly as
    // before; only the new "PublicApi" policy below opts into this scheme.
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });

builder.Services.AddAuthorization(options =>
{
    // Any authenticated platform user (all 5 roles in PlatformRole) - read-only
    // console access. Mutating actions (create/suspend/reactivate tenant) use
    // the stricter PlatformManage policy below.
    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("scope", "platform_admin"));

    options.AddPolicy("PlatformManage", policy =>
        policy.RequireClaim("scope", "platform_admin")
              .RequireClaim("platform_role", nameof(PlatformRole.Owner), nameof(PlatformRole.PlatformAdmin)));

    // Module 5.6 - Adding platform users (including additional Owners) is
    // Owner-only, stricter than PlatformManage (which also lets a
    // PlatformAdmin through) - granting platform access is a step above
    // ordinary tenant management and PlatformAdmin must not be able to
    // mint its own peers or a new Owner.
    options.AddPolicy("PlatformOwnerOnly", policy =>
        policy.RequireClaim("scope", "platform_admin")
              .RequireClaim("platform_role", nameof(PlatformRole.Owner)));

    // Module 7 - Customer/End-User Portal. A portal customer's token carries
    // scope=portal_customer and nothing else that would satisfy the policies
    // above or a staff [Authorize(Roles = ...)] check - see JwtTokenService.
    options.AddPolicy("PortalCustomer", policy =>
        policy.RequireClaim("scope", "portal_customer"));

    // Every tenant-scoped staff controller (Tickets, Tenant, SlaPolicies,
    // Categories) must use this policy instead of a bare [Authorize] at the
    // controller level. Bare [Authorize] only checks IsAuthenticated - it
    // does NOT inspect the Role claim, so before this policy existed, a
    // portal customer's token (which now carries a valid tenant_id claim,
    // same as a staff AppUser token) could pass a bare [Authorize] check and
    // reach the full tenant-wide staff endpoints, including internal-only
    // comments, under its own valid tenant context. Only AppUser tokens ever
    // carry a Role claim (see JwtTokenService.CreateAccessToken) - Platform
    // and Portal tokens never do - so requiring its mere presence (any
    // value) is enough to exclude both non-staff token types here.
    options.AddPolicy("TenantStaff", policy =>
        policy.RequireClaim(ClaimTypes.Role));

    // Module 12 - Roles & Permissions: one policy per Permission enum
    // value, each delegating to PermissionAuthorizationHandler (built-in
    // Admin/Manager roles always pass; anyone else needs a custom role
    // granting that specific permission - see the handler for the full
    // reasoning). The enum is small and fixed in code, so pre-registering
    // every value's policy here is simpler than a dynamic policy provider.
    foreach (var permission in Enum.GetValues<Permission>())
    {
        options.AddPolicy($"Permission:{permission}", policy =>
            policy.Requirements.Add(new PermissionRequirement(permission)));
    }

    // Module 11 - Integrations & Public API. Explicitly pinned to the
    // "ApiKey" scheme only - a staff/portal JWT (even a valid, unexpired
    // one) must NOT be usable against /api/v1/tickets, and an API key must
    // not be usable against any staff/portal endpoint. Keeping these two
    // credential types fully non-interchangeable is the point of having a
    // separate scheme at all.
    options.AddPolicy("PublicApi", policy =>
        policy.AddAuthenticationSchemes("ApiKey")
              .RequireClaim("scope", "public_api"));

    // Module 5.2 - Plans & Billing Administration. Spec section 5.6: "Billing
    // Admin (billing only, no impersonation)" - so BillingAdmin joins
    // Owner/PlatformAdmin for billing mutations (credits, plan overrides),
    // narrower than PlatformManage (which BillingAdmin does NOT satisfy -
    // tenant create/suspend/reactivate stays Owner/PlatformAdmin only) and
    // broader than the plain PlatformAdmin policy (any role, read-only).
    options.AddPolicy("PlatformBilling", policy =>
        policy.RequireClaim("scope", "platform_admin")
              .RequireClaim("platform_role", nameof(PlatformRole.Owner), nameof(PlatformRole.PlatformAdmin), nameof(PlatformRole.BillingAdmin)));

    // Module 5.1 - Tenant impersonation. Spec section 5.6: "Support Engineer
    // (impersonation + read)" - so SupportEngineer joins Owner/PlatformAdmin
    // here, but BillingAdmin and ReadOnlyAnalyst do not (matching the spec's
    // explicit "Billing Admin: billing only, no impersonation" and
    // ReadOnlyAnalyst's read-only framing).
    options.AddPolicy("PlatformImpersonate", policy =>
        policy.RequireClaim("scope", "platform_admin")
              .RequireClaim("platform_role", nameof(PlatformRole.Owner), nameof(PlatformRole.PlatformAdmin), nameof(PlatformRole.SupportEngineer)));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendApp", policy =>
        policy.WithOrigins(
                builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("FrontendApp");
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
