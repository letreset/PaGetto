using System.Text.Json.Nodes;
using PaGetto.Core.CocoaPods;
using Xunit;

namespace PaGetto.Core.Tests.CocoaPods;

public class CocoaPodsPodspecTests
{
    public class Read
    {
        [Fact]
        public void ReadsTheNameVersionAndSummary()
        {
            var podspec = CocoaPodsPodspec.Read("""{"name": "MyLib", "version": "1.0.0", "summary": "A library", "source": {"git": "https://example.com/mylib.git", "tag": "1.0.0"}}""");

            Assert.Equal("MyLib", CocoaPodsPodspec.GetName(podspec));
            Assert.Equal("1.0.0", CocoaPodsPodspec.GetVersion(podspec));
            Assert.Equal("A library", CocoaPodsPodspec.GetSummary(podspec));
        }

        [Theory]
        [InlineData("Pod::Spec.new do |s|", "not valid JSON")]
        [InlineData("[]", "not a JSON object")]
        [InlineData("""{"version": "1.0.0"}""", "(none) is not a pod name")]
        [InlineData("""{"name": "My Lib", "version": "1.0.0"}""", "My Lib is not a pod name")]
        [InlineData("""{"name": "MyLib"}""", "(none) is not a version")]
        [InlineData("""{"name": "MyLib", "version": "v1"}""", "v1 is not a version")]
        public void RejectsInvalidPodspecsWithAMessage(string json, string message)
        {
            var exception = Assert.Throws<InvalidPodspecException>(() => CocoaPodsPodspec.Read(json));

            Assert.Contains(message, exception.Message);
        }
    }

    public class TryGetArchiveType
    {
        [Theory]
        [InlineData("MyLib.zip", "zip")]
        [InlineData("MyLib-1.0.0.tar.gz", "tgz")]
        [InlineData("MyLib.TGZ", "tgz")]
        [InlineData("MyLib.tar.bz2", "tbz")]
        [InlineData("MyLib.txz", "txz")]
        [InlineData("MyLib.tar", "tar")]
        public void KnowsTheArchivesCocoaPodsUnpacks(string fileName, string type)
        {
            Assert.True(CocoaPodsPodspec.TryGetArchiveType(fileName, out var actual));
            Assert.Equal(type, actual);
        }

        [Theory]
        [InlineData("MyLib.dmg")]
        [InlineData("MyLib.7z")]
        [InlineData(null)]
        public void RejectsOtherFiles(string fileName)
        {
            Assert.False(CocoaPodsPodspec.TryGetArchiveType(fileName, out _));
        }
    }

    public class StoredSource
    {
        [Fact]
        public void PointsTheSourceAtTheStoredArchive()
        {
            var podspec = CocoaPodsPodspec.Read("""{"name": "MyLib", "version": "1.0.0", "source": {"http": "https://example.com/x.zip", "flatten": true}}""");

            CocoaPodsPodspec.SetStoredSource(podspec, "zip", "abc");
            var stored = podspec.ToJsonString();
            var served = JsonNode.Parse(CocoaPodsPodspec.AddSourceUrl(stored, "https://feed/sources/MyLib/1.0.0/MyLib-1.0.0.zip"))!;

            Assert.Equal("zip", CocoaPodsPodspec.GetStoredArchiveType(stored));
            Assert.Equal("""{"http":"https://feed/sources/MyLib/1.0.0/MyLib-1.0.0.zip","type":"zip","sha256":"abc","flatten":true}""", served["source"]!.ToJsonString());
            Assert.Equal("MyLib", served["name"]!.GetValue<string>());
        }
    }
}
