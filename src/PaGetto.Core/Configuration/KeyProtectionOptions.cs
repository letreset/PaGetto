namespace PaGetto.Core.Configuration;

/// <summary>
/// Encrypts the Data Protection key ring (sign-in cookies, antiforgery tokens) with a certificate
/// before it is written to storage. Bound to the <c>DataProtection</c> section. Without a
/// certificate the keys are stored unencrypted.
/// </summary>
public class KeyProtectionOptions
{
    /// <summary>
    /// Path to a PFX (PKCS#12) file with the certificate and its private key.
    /// </summary>
    public string CertificatePath { get; set; }

    /// <summary>
    /// The password of <see cref="CertificatePath"/>, if it has one.
    /// </summary>
    public string CertificatePassword { get; set; }

    /// <summary>
    /// Thumbprint of a certificate in the "My" store of the local machine or the current user
    /// (Windows, e.g. IIS). Use instead of <see cref="CertificatePath"/>.
    /// </summary>
    public string CertificateThumbprint { get; set; }
}
