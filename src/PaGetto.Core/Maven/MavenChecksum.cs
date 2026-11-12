namespace PaGetto.Core.Maven;

/// <summary>
/// The checksum files a Maven repository has next to every file, e.g. <c>my-lib-1.0.jar.sha1</c>.
/// The server computes them; clients upload them too, and those are checked.
/// </summary>
public enum MavenChecksum
{
    Sha1,
    Md5,
    Sha256,
    Sha512,
}
