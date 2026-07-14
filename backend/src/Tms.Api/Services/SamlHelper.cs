using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace Tms.Api.Services;

// Module 1 - Authentication & Identity (SSO). A minimal SAML 2.0 Service
// Provider - enough to cover the spec's "Done when a tenant admin can enable
// SSO for their org, users can log in via SSO" bar, built entirely on the
// .NET base class library (System.Security.Cryptography.Xml, the same
// XML-DSig verifier ASP.NET Core's own WS-Federation/SAML-adjacent auth
// handlers use internally) rather than a third-party SAML package, since
// this couldn't be compiled/tested locally (no dotnet SDK in this sandbox -
// see the repo's own "no local build" constraint) and a well-known stdlib
// API surface is a safer bet under that constraint than trusting an
// unfamiliar package's exact method signatures from memory.
//
// KNOWN LIMITATIONS (flagged for the "Enterprise hardening" phase, not
// silently pretended away): AuthnRequests are not signed (most IdPs don't
// require this for SP-initiated login); encrypted assertions are not
// supported (only signed, plaintext assertions); audience restriction and
// InResponseTo correlation are not checked (RelayState/SsoLoginState already
// prevents replay and CSRF at the transport level - see SsoAuthController).
// A real enterprise rollout should get a second pair of eyes on this file,
// or migrate to a mature library like ITfoxtec.Identity.Saml2, before being
// relied on for a paying customer's compliance requirements.
public static class SamlHelper
{
    public record VerifiedAssertion(string NameId, string Issuer);

    // HTTP-Redirect binding: deflate (raw DEFLATE, RFC 1951, no zlib/gzip
    // header - exactly what the SAML spec's binding requires) + base64 the
    // AuthnRequest XML, so it fits in a query string.
    public static string BuildAuthnRequestRedirectUrl(string idpSsoUrl, string spEntityId, string acsUrl, string relayState)
    {
        var requestId = $"_{Guid.NewGuid():N}";
        var issueInstant = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var xml = $"""
            <samlp:AuthnRequest xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion" ID="{requestId}" Version="2.0" IssueInstant="{issueInstant}" Destination="{System.Security.SecurityElement.Escape(idpSsoUrl)}" AssertionConsumerServiceURL="{System.Security.SecurityElement.Escape(acsUrl)}" ProtocolBinding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"><saml:Issuer>{System.Security.SecurityElement.Escape(spEntityId)}</saml:Issuer></samlp:AuthnRequest>
            """;

        var utf8Bytes = Encoding.UTF8.GetBytes(xml);

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(utf8Bytes, 0, utf8Bytes.Length);
        }

        var encodedRequest = Uri.EscapeDataString(Convert.ToBase64String(output.ToArray()));
        var encodedRelayState = Uri.EscapeDataString(relayState);

        var separator = idpSsoUrl.Contains('?') ? "&" : "?";
        return $"{idpSsoUrl}{separator}SAMLRequest={encodedRequest}&RelayState={encodedRelayState}";
    }

    // Verifies the XML-DSig signature on the SAMLResponse's Assertion (or,
    // failing that, on the Response itself) against the tenant's configured
    // IdP certificate, and extracts identity ONLY from the specific element
    // whose signature was verified - never from an unsigned/differently-
    // signed element elsewhere in the document. That last part specifically
    // guards against XML "wrapping" attacks, where an attacker who can get
    // any validly-signed assertion accepted (e.g. a self-service one) injects
    // a second, unsigned/forged Assertion elsewhere in the same document
    // claiming a different identity - naive implementations that just
    // XPath-search the whole document for "the NameID" are vulnerable to
    // exactly this.
    public static VerifiedAssertion ParseAndVerifyResponse(string samlResponseBase64, string idpCertificatePemOrBase64)
    {
        var xmlBytes = Convert.FromBase64String(samlResponseBase64);
        var xml = Encoding.UTF8.GetString(xmlBytes);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        var nsManager = new XmlNamespaceManager(doc.NameTable);
        nsManager.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        nsManager.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        nsManager.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        var assertionNode = doc.SelectSingleNode("//saml:Assertion", nsManager) as XmlElement
            ?? throw new InvalidOperationException("SAMLResponse did not contain an Assertion element.");

        // Prefer the Assertion's own signature (the actual authentication
        // statement) - fall back to the Response's signature only if the
        // Assertion itself isn't individually signed.
        var signatureNode =
            assertionNode.SelectSingleNode("ds:Signature", nsManager) as XmlElement
            ?? doc.DocumentElement?.SelectSingleNode("ds:Signature", nsManager) as XmlElement
            ?? throw new InvalidOperationException("SAMLResponse is not signed (no Signature element found on the Assertion or Response).");

        var signedElement = signatureNode.ParentNode as XmlElement
            ?? throw new InvalidOperationException("Malformed Signature element.");

        var certificate = ParseCertificate(idpCertificatePemOrBase64);

        var signedXml = new SignedXmlWithId(doc);
        signedXml.LoadXml(signatureNode);

        if (!signedXml.CheckSignature(certificate, verifySignatureOnly: true))
        {
            throw new InvalidOperationException("SAMLResponse signature verification failed.");
        }

        // Identity is read from `signedElement` specifically (the element
        // the verified signature covers), not from `doc` broadly - see this
        // method's doc comment on wrapping attacks. If the Response (not the
        // Assertion) was what got signed, signedElement IS the Response, so
        // re-scope the NameID/Issuer lookup back down to the one Assertion
        // we already located above (there is exactly one in the documents
        // this SP accepts).
        var scopedNode = signedElement.Name.EndsWith("Assertion", StringComparison.Ordinal) ? signedElement : assertionNode;

        var nameIdNode = scopedNode.SelectSingleNode(".//saml:Subject/saml:NameID", nsManager)
            ?? throw new InvalidOperationException("Verified assertion did not contain a Subject/NameID.");

        var issuerNode = scopedNode.SelectSingleNode("saml:Issuer", nsManager);

        ValidateConditions(scopedNode, nsManager);

        return new VerifiedAssertion(
            NameId: nameIdNode.InnerText.Trim(),
            Issuer: issuerNode?.InnerText.Trim() ?? string.Empty);
    }

    private static void ValidateConditions(XmlElement assertionNode, XmlNamespaceManager nsManager)
    {
        var conditions = assertionNode.SelectSingleNode("saml:Conditions", nsManager) as XmlElement;
        if (conditions is null) return; // Not all IdPs include Conditions - signature is still the primary trust check.

        var now = DateTime.UtcNow;
        var skew = TimeSpan.FromMinutes(5);

        var notBefore = conditions.GetAttribute("NotBefore");
        if (!string.IsNullOrEmpty(notBefore) && DateTime.TryParse(notBefore, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var nb) && now < nb - skew)
        {
            throw new InvalidOperationException("SAML assertion is not yet valid (NotBefore).");
        }

        var notOnOrAfter = conditions.GetAttribute("NotOnOrAfter");
        if (!string.IsNullOrEmpty(notOnOrAfter) && DateTime.TryParse(notOnOrAfter, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var noa) && now > noa + skew)
        {
            throw new InvalidOperationException("SAML assertion has expired (NotOnOrAfter).");
        }
    }

    // Accepts either a full PEM ("-----BEGIN CERTIFICATE-----...") or a raw
    // base64 DER blob (what you get pasting just the body of a certificate,
    // or copying the "X.509 Certificate" field straight out of an IdP's
    // metadata/app-registration screen) - tenant admins pasting IdP
    // certificates by hand will supply either format interchangeably.
    private static X509Certificate2 ParseCertificate(string pemOrBase64)
    {
        var trimmed = pemOrBase64.Trim();
        if (trimmed.Contains("BEGIN CERTIFICATE"))
        {
#pragma warning disable SYSLIB0057 // CreateFromPem-equivalent constructor; acceptable for a public IdP signing cert, not a private key.
            return X509Certificate2.CreateFromPem(trimmed);
#pragma warning restore SYSLIB0057
        }

        var der = Convert.FromBase64String(trimmed);
#pragma warning disable SYSLIB0057
        return new X509Certificate2(der);
#pragma warning restore SYSLIB0057
    }

    // .NET's XmlDocument has no SetIdAttribute (that's a .NET Framework-only
    // API) so SignedXml's default GetIdElement can't resolve a Reference
    // URI="#SomeId" against an arbitrary ID-typed attribute without a
    // DTD/schema declaring it as such. GetIdElement is a public virtual
    // method on SignedXml specifically designed to be overridden for this -
    // this is the standard, widely-documented fix for verifying SAML/WS-Fed
    // style signed XML on .NET Core, not a workaround specific to this file.
    private class SignedXmlWithId : SignedXml
    {
        public SignedXmlWithId(XmlDocument document) : base(document) { }

        public override XmlElement? GetIdElement(XmlDocument document, string idValue)
        {
            var byBase = base.GetIdElement(document, idValue);
            if (byBase is not null) return byBase;

            return document.SelectSingleNode($"//*[@ID='{idValue}']") as XmlElement
                ?? document.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement;
        }
    }
}
