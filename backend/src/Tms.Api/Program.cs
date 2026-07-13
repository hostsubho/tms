using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    });

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
