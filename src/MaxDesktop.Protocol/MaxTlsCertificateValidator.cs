using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace MaxDesktop.Protocol;

/// <summary>
/// Validates MAX TLS using the system certificate store, or (only if the system
/// chain is untrusted) the Russian Trusted Root CA bundled with the client.
/// Certificate name mismatches and absent certificates always fail.
/// This root is scoped to the MAX TCP TLS client, not installed system-wide.
/// </summary>
public static class MaxTlsCertificateValidator
{
    private const string ResourceName =
        "MaxDesktop.Protocol.Certificates.rootca_ssl_rsa2022.crt";

    public static bool Validate(object sender, X509Certificate? certificate,
        X509Chain? systemChain, SslPolicyErrors errors)
    {
        if (certificate is null ||
            errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) ||
            errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            return false;

        if (errors == SslPolicyErrors.None) return true;
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors) return false;

        try
        {
            using Stream? pemStream =
                typeof(MaxTlsCertificateValidator).Assembly.GetManifestResourceStream(ResourceName);
            if (pemStream is null) return false;

            using var reader = new StreamReader(pemStream);
            using X509Certificate2 authority = X509Certificate2.CreateFromPem(reader.ReadToEnd());
            using X509Certificate2 leaf = new(certificate);
            using var trustedChain = new X509Chain();
            trustedChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            trustedChain.ChainPolicy.CustomTrustStore.Add(authority);
            trustedChain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            trustedChain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            trustedChain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            trustedChain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);

            // Reuse intermediates supplied by the remote endpoint, but never
            // treat them as trusted roots.
            if (systemChain is not null)
            {
                foreach (X509ChainElement element in systemChain.ChainElements)
                {
                    if (!element.Certificate.RawData.AsSpan().SequenceEqual(leaf.RawData))
                        trustedChain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }

            if (!trustedChain.Build(leaf)) return false;
            var elements = trustedChain.ChainElements;
            return elements.Count > 0 &&
                elements[elements.Count - 1].Certificate.RawData.AsSpan()
                    .SequenceEqual(authority.RawData);
        }
        catch (Exception ex) when (ex is IOException or
            System.Security.Cryptography.CryptographicException or
            InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
