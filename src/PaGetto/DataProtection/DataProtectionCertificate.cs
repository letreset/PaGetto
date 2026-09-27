using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using PaGetto.Core.Configuration;

namespace PaGetto.DataProtection;

/// <summary>
/// Loads the certificate that encrypts the Data Protection key ring, from a PFX file or from the
/// Windows certificate store.
/// </summary>
internal static class DataProtectionCertificate
{
    /// <summary>
    /// Returns the configured certificate, or null when none is configured. A missing file is left
    /// to startup validation, which reports it.
    /// </summary>
    public static X509Certificate2 Load(KeyProtectionOptions options)
    {
        if (!string.IsNullOrEmpty(options?.CertificatePath))
        {
            if (!File.Exists(options.CertificatePath))
                return null;

            return X509CertificateLoader.LoadPkcs12FromFile(
                options.CertificatePath, options.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        }

        if (!string.IsNullOrEmpty(options?.CertificateThumbprint))
        {
            return FindInStore(StoreLocation.LocalMachine, options.CertificateThumbprint)
                ?? FindInStore(StoreLocation.CurrentUser, options.CertificateThumbprint)
                ?? throw new InvalidOperationException(
                    $"The DataProtection certificate with thumbprint {options.CertificateThumbprint} was not found in the LocalMachine or CurrentUser 'My' store.");
        }

        return null;
    }

    private static X509Certificate2 FindInStore(StoreLocation location, string thumbprint)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint.Replace(" ", string.Empty), validOnly: false);
        return matches.Count > 0 ? matches[0] : null;
    }
}
