using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Tests.Support;

/// <summary>
/// What tests of PyPI feeds share: creating a feed, building wheels and sdists, and uploading them
/// the way <c>twine upload</c> does.
/// </summary>
internal static class PyPITestClient
{
    public const string JsonAccept = "application/vnd.pypi.simple.v1+json";

    /// <summary>
    /// Creates a PyPI feed, optionally with one mirror.
    /// </summary>
    public static async Task<Feed> CreatePyPIFeedAsync(
        this WebApplicationFactory<Startup> factory,
        string slug,
        string mirrorSource = null,
        Action<Feed> configure = null)
    {
        using var scope = factory.Services.CreateScope();
        var feedService = scope.ServiceProvider.GetRequiredService<IFeedService>();

        var feed = new Feed { Slug = slug, Name = slug, Type = FeedType.PyPI };
        if (mirrorSource != null)
        {
            feed.Mirrors.Add(new FeedMirror { Enabled = true, PackageSource = mirrorSource });
        }

        configure?.Invoke(feed);

        return await feedService.CreateFeedAsync(feed, CancellationToken.None);
    }

    public static string SimpleUrl(string feedSlug)
    {
        return $"feeds/{feedSlug}/pypi/simple/";
    }

    public static string ProjectUrl(string feedSlug, string name)
    {
        return $"{SimpleUrl(feedSlug)}{name}/";
    }

    public static string FileUrl(string feedSlug, string name, string version, string fileName)
    {
        return $"feeds/{feedSlug}/pypi/files/{name}/{version}/{fileName}";
    }

    public static string UploadUrl(string feedSlug)
    {
        return $"feeds/{feedSlug}/pypi/legacy/";
    }

    /// <summary>
    /// The name of a wheel as build backends write it: the normalized name with underscores.
    /// </summary>
    public static string WheelFileName(string name, string version)
    {
        return $"{FileNamePart(name)}-{version}-py3-none-any.whl";
    }

    public static string SdistFileName(string name, string version)
    {
        return $"{FileNamePart(name)}-{version}.tar.gz";
    }

    /// <summary>
    /// Core metadata as build backends write it.
    /// </summary>
    public static string Metadata(string name, string version, string summary = "A test package.")
    {
        return $"""
            Metadata-Version: 2.1
            Name: {name}
            Version: {version}
            Summary: {summary}
            Requires-Python: >=3.9
            Requires-Dist: requests>=2.0
            Project-URL: Source, https://example.com/{name}
            Description-Content-Type: text/markdown

            # {name}

            The readme of {name}.
            """;
    }

    /// <summary>
    /// A wheel: a zip file with the package and its .dist-info folder.
    /// </summary>
    public static byte[] Wheel(string name, string version, string summary = "A test package.", string marker = null)
    {
        var folder = FileNamePart(name);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(zip, $"{folder}/__init__.py", $"# {marker ?? version}\n");
            AddZipEntry(zip, $"{folder}-{version}.dist-info/METADATA", Metadata(name, version, summary));
            AddZipEntry(zip, $"{folder}-{version}.dist-info/WHEEL", "Wheel-Version: 1.0\nGenerator: test\nRoot-Is-Purelib: true\nTag: py3-none-any\n");
        }

        return stream.ToArray();
    }

    /// <summary>
    /// An sdist: a gzipped tar file with the project in a folder, and its PKG-INFO.
    /// </summary>
    public static byte[] Sdist(string name, string version, string summary = "A test package.")
    {
        var folder = $"{FileNamePart(name)}-{version}";

        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Ustar, leaveOpen: true))
        {
            AddTarEntry(tar, $"{folder}/pyproject.toml", "[project]\n");
            AddTarEntry(tar, $"{folder}/PKG-INFO", Metadata(name, version, summary));
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Uploads a file as twine does: a multipart form with the file as "content" and its metadata.
    /// </summary>
    public static async Task<HttpResponseMessage> UploadPythonAsync(
        this HttpClient client,
        string feedSlug,
        string name,
        string version,
        string fileName,
        byte[] content,
        string sha256Digest = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("file_upload"), ":action");
        form.Add(new StringContent("1"), "protocol_version");
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(version), "version");
        form.Add(new StringContent(fileName.EndsWith(".whl", StringComparison.Ordinal) ? "bdist_wheel" : "sdist"), "filetype");
        form.Add(new StringContent("2.1"), "metadata_version");
        form.Add(new StringContent("A test package."), "summary");
        form.Add(new StringContent(sha256Digest ?? Sha256(content)), "sha256_digest");

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "content", fileName);

        return await client.PostAsync(UploadUrl(feedSlug), form);
    }

    /// <summary>
    /// Uploads a wheel and an sdist of a version, and asserts that both succeed.
    /// </summary>
    /// <returns>The wheel that was uploaded.</returns>
    public static async Task<byte[]> PublishPythonAsync(this HttpClient client, string feedSlug, string name, string version, string summary = "A test package.")
    {
        var wheel = Wheel(name, version, summary);

        foreach (var (fileName, content) in new[] { (WheelFileName(name, version), wheel), (SdistFileName(name, version), Sdist(name, version, summary)) })
        {
            using var response = await client.UploadPythonAsync(feedSlug, name, version, fileName, content);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Upload of {fileName}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        return wheel;
    }

    /// <summary>
    /// Gets a page of the simple API in its JSON form.
    /// </summary>
    public static async Task<JsonDocument> GetSimpleJsonAsync(this HttpClient client, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(JsonAccept);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonAccept, response.Content.Headers.ContentType?.MediaType);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public static string Sha256(byte[] content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private static void AddZipEntry(ZipArchive zip, string path, string content)
    {
        // A fixed time, so the same wheel has the same bytes.
        var entry = zip.CreateEntry(path);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void AddTarEntry(TarWriter tar, string path, string content)
    {
        // A fixed time, so the same sdist has the same bytes, as a rebuilt one would not.
        tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, path)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            ModificationTime = DateTimeOffset.UnixEpoch,
        });
    }

    private static string FileNamePart(string name)
    {
        return PyPIPackageName.Normalize(name).Replace('-', '_');
    }
}
