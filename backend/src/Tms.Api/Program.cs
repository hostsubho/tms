using System.Text;
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
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddDbContext<TmsDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddControllers();
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
    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("scope", "platform_admin"));
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
