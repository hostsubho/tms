using System.Net;
using System.Net.Sockets;
using System.Linq;

namespace Tms.Api.Services;

// Module 11 - Integrations & Public API. Basic SSRF mitigation for outbound
// webhook URLs, applied once at subscription-creation time (see
// WebhooksController.CreateWebhook) - NOT re-checked on every delivery. A
// DNS-rebinding attack (a URL that resolves to a safe public IP when the
// admin adds it, then to an internal IP later at delivery time) would slip
// past this; fully closing that gap would need either re-resolving and
// re-validating immediately before every single delivery, or an HttpClient
// configured to validate the connected socket's resolved IP at connect
// time. Documented here as a known, accepted limitation rather than silently
// shipped as if fully solved - consistent with how this codebase scopes
// down and documents gaps elsewhere (e.g. Module 5's own "run webhook"
// action being cut for this exact SSRF concern).
public static class WebhookUrlValidator
{
    public static async Task<string?> ValidateAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "URL is not a valid absolute URI.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return "Webhook URLs must use https.";
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host);
        }
        catch (SocketException)
        {
            return "Could not resolve the webhook host.";
        }

        if (addresses.Length == 0)
        {
            return "Could not resolve the webhook host.";
        }

        if (addresses.Any(IsDisallowed))
        {
            return "Webhook URL resolves to a private, loopback, or link-local address, which is not allowed.";
        }

        return null;
    }

    private static bool IsDisallowed(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        // Unwraps both the standard "IPv4-mapped" form (::ffff:a.b.c.d,
        // IsIPv4MappedToIPv6) and the older, deprecated "IPv4-compatible"
        // form (::a.b.c.d - first 12 bytes zero, last 4 the embedded
        // address) - both are ways an attacker could smuggle a private
        // IPv4 target past a check that only ever looked at IPv6-shaped
        // rules. Without this, e.g. https://[::7f00:1]/ (embedding
        // 127.0.0.1) would sail through.
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6Bytes = address.GetAddressBytes();
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            else if (v6Bytes.Take(12).All(b => b == 0))
            {
                address = new IPAddress(v6Bytes[12..]);
            }
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10) return true; // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true; // 172.16.0.0/12
            if (bytes[0] == 192 && bytes[1] == 168) return true; // 192.168.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return true; // 169.254.0.0/16 link-local
            if (bytes[0] == 0) return true; // 0.0.0.0/8, also catches bare "::" (unspecified)
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address)) return true;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xfe) == 0xfc) return true; // fc00::/7 unique local
        }

        return false;
    }
}
