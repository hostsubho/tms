using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Tms.Api.Services;

// Module 1 - Authentication & Identity (SSO). Deliberately hand-rolled
// against the standard OIDC discovery document / JWKS endpoint / token
// endpoint (all plain HTTP + System.Text.Json) rather than pulling in
// Microsoft.AspNetCore.Authentication.OpenIdConnect, which is designed around
// a single cookie-based challenge/callback for one fixed IdP wired up at
// startup - this app needs a different IdP per tenant, resolved at request
// time from TenantSsoConfig, which doesn't fit that middleware's model.
// Signature validation itself still goes through the same
// System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Tokens APIs
// ASP.NET Core's own JWT bearer handler uses internally - not hand-rolled
// crypto, just driven manually instead of via middleware.
public class OidcService : IOidcService
{
    private readonly HttpClient _httpClient;

    public OidcService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OidcDiscoveryDocument> GetDiscoveryDocumentAsync(string authority, CancellationToken ct)
    {
        var url = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        return new OidcDiscoveryDocument(
            Issuer: root.GetProperty("issuer").GetString()!,
            AuthorizationEndpoint: root.GetProperty("authorization_endpoint").GetString()!,
            TokenEndpoint: root.GetProperty("token_endpoint").GetString()!,
            JwksUri: root.GetProperty("jwks_uri").GetString()!);
    }

    public async Task<string> ExchangeCodeForIdTokenAsync(
        OidcDiscoveryDocument discovery,
        string clientId,
        string clientSecret,
        string code,
        string redirectUri,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"IdP token endpoint returned {(int)response.StatusCode}: {body}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenProp))
        {
            throw new InvalidOperationException("IdP token response did not include an id_token.");
        }

        return idTokenProp.GetString()!;
    }

    public async Task<ClaimsPrincipal> ValidateIdTokenAsync(
        OidcDiscoveryDocument discovery,
        string idToken,
        string clientId,
        CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(discovery.JwksUri, ct);
        response.EnsureSuccessStatusCode();
        var jwksJson = await response.Content.ReadAsStringAsync(ct);
        var jwks = new JsonWebKeySet(jwksJson);

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = discovery.Issuer,
            ValidateAudience = true,
            ValidAudience = clientId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = jwks.Keys,
            ClockSkew = TimeSpan.FromSeconds(60),
        };

        // Throws SecurityTokenException (or a subclass) on any signature,
        // issuer, audience, or expiry mismatch - propagated to the caller as
        // a hard failure, never swallowed into a "treat as unauthenticated"
        // fallback that could mask a real validation bug as a normal login
        // failure.
        var principal = handler.ValidateToken(idToken, validationParameters, out _);
        return principal;
    }
}
