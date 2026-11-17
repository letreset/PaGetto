using System.Collections.Generic;
using PaGetto.Core.CocoaPods;
using Xunit;

namespace PaGetto.Core.Tests.CocoaPods;

public class CocoaPodsSpecRepositoryTests
{
    public class TryParseIndexFileName
    {
        [Fact]
        public void ReadsTheShard()
        {
            Assert.True(CocoaPodsSpecRepository.TryParseIndexFileName("all_pods_versions_d_a_2.txt", out var shard));
            Assert.Equal(new[] { "d", "a", "2" }, shard);
            Assert.Equal("all_pods_versions_d_a_2.txt", CocoaPodsSpecRepository.GetIndexFileName(shard));
        }

        [Theory]
        [InlineData("all_pods_versions_d_a.txt")]
        [InlineData("all_pods_versions_D_a_2.txt")]
        [InlineData("all_pods_versions_g_a_2.txt")]
        [InlineData("all_pods.txt")]
        [InlineData(null)]
        public void RejectsOtherFiles(string fileName)
        {
            Assert.False(CocoaPodsSpecRepository.TryParseIndexFileName(fileName, out _));
        }
    }

    public class GetPodspecPath
    {
        [Fact]
        public void IsBelowTheShardOfThePod()
        {
            Assert.Equal("Specs/d/a/2/Alamofire/5.9.1/Alamofire.podspec.json", CocoaPodsSpecRepository.GetPodspecPath("Alamofire", "5.9.1"));
        }
    }

    public class WriteIndex
    {
        [Fact]
        public void WritesALinePerPodInOrder()
        {
            var index = CocoaPodsSpecRepository.WriteIndex(
            [
                KeyValuePair.Create("Zeta", (IReadOnlyList<string>)["1.0.0"]),
                KeyValuePair.Create("Alpha", (IReadOnlyList<string>)["1.0.0", "1.1.0"]),
            ]);

            Assert.Equal("Alpha/1.0.0/1.1.0\nZeta/1.0.0\n", index);
        }
    }

    public class ReadIndex
    {
        [Fact]
        public void ReadsThePodsAndSkipsWhatCantBeServed()
        {
            var pods = CocoaPodsSpecRepository.ReadIndex("Alpha/1.0.0/1.1.0\r\nFLYKìt/1.0\nBeta/not a version/2.0.0\nGamma/x\n\n");

            Assert.Equal(new[] { "1.0.0", "1.1.0" }, pods["Alpha"]);
            Assert.Equal(new[] { "2.0.0" }, pods["Beta"]);
            Assert.Equal(2, pods.Count);
        }
    }
}
