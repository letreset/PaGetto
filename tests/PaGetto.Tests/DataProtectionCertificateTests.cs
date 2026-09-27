using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Storage;
using PaGetto.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// DataProtection:CertificatePath encrypts the key ring that is written to storage.
/// </summary>
public class DataProtectionCertificateTests : IDisposable
{
    private const string CertificatePassword = "test-password";

    private readonly string _certificatePath;
    private readonly PaGettoApplication _app;

    public DataProtectionCertificateTests(ITestOutputHelper output)
    {
        _certificatePath = Path.Combine(Path.GetTempPath(), "PaGettoTests", $"{Guid.NewGuid():N}.pfx");
        Directory.CreateDirectory(Path.GetDirectoryName(_certificatePath));

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=PaGetto test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(_certificatePath, certificate.Export(X509ContentType.Pfx, CertificatePassword));

        _app = new PaGettoApplication(output, null, dict =>
        {
            dict["DataProtection:CertificatePath"] = _certificatePath;
            dict["DataProtection:CertificatePassword"] = CertificatePassword;
        });
    }

    [Fact]
    public async Task EncryptsTheStoredKeyRing()
    {
        var protector = _app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        var protectedValue = protector.Protect("secret");

        Assert.Equal("secret", protector.Unprotect(protectedValue));

        using var scope = _app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        await using var stream = await storage.GetAsync("dataprotection/keyring.xml", CancellationToken.None);
        using var reader = new StreamReader(stream);
        var keyRing = await reader.ReadToEndAsync();

        Assert.Contains("EncryptedData", keyRing);
        Assert.DoesNotContain("<masterKey", keyRing);
    }

    public void Dispose()
    {
        _app.Dispose();
        File.Delete(_certificatePath);
    }
}
