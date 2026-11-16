using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PaGetto.Core.Hex;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class ErlangExternalTermTests
{
    public class Decode
    {
        [Fact]
        public void ReadsTheMapOfTermToBinary()
        {
            // term_to_binary(#{<<"message">> => <<"m">>, <<"reason">> => <<"security">>})
            var data = new byte[] { 131, 116, 0, 0, 0, 2 }
                .Concat(Binary("message")).Concat(Binary("m"))
                .Concat(Binary("reason")).Concat(Binary("security"))
                .ToArray();

            var map = Assert.IsType<Dictionary<string, object>>(ErlangExternalTerm.Decode(data));

            Assert.Equal("m", map["message"]);
            Assert.Equal("security", map["reason"]);
        }

        [Fact]
        public void ReadsAtomsIntegersListsAndTuples()
        {
            // term_to_binary({ok, [1, 300, -1], true, nil, "ab"})
            var data = new byte[]
            {
                131, 104, 5,
                119, 2, (byte)'o', (byte)'k',
                108, 0, 0, 0, 3, 97, 1, 98, 0, 0, 1, 44, 98, 255, 255, 255, 255, 106,
                119, 4, (byte)'t', (byte)'r', (byte)'u', (byte)'e',
                119, 3, (byte)'n', (byte)'i', (byte)'l',
                107, 0, 2, (byte)'a', (byte)'b',
            };

            var tuple = Assert.IsType<object[]>(ErlangExternalTerm.Decode(data));

            Assert.Equal("ok", tuple[0]);
            Assert.Equal(new List<object> { 1L, 300L, -1L }, tuple[1]);
            Assert.Equal(true, tuple[2]);
            Assert.Null(tuple[3]);
            Assert.Equal("ab", tuple[4]);
        }

        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 130, 106 })]
        [InlineData(new byte[] { 131, 109, 0, 0, 0, 5, 1 })]
        [InlineData(new byte[] { 131, 108, 255, 255, 255, 255 })]
        [InlineData(new byte[] { 131, 80, 0, 0, 0, 1 })]
        [InlineData(new byte[] { 131, 106, 106 })]
        public void RejectsDataThatIsNotATerm(byte[] data)
        {
            Assert.Throws<FormatException>(() => ErlangExternalTerm.Decode(data));
        }
    }

    public class Encode
    {
        [Fact]
        public void WritesWhatDecodeReads()
        {
            var value = new Dictionary<string, object>
            {
                ["text"] = "héllo",
                ["small"] = 7,
                ["large"] = 70000,
                ["huge"] = 1L << 40,
                ["negative"] = -1L << 40,
                ["flag"] = false,
                ["none"] = null,
                ["list"] = new List<object> { "a", new Dictionary<string, object> { ["b"] = 1 } },
                ["empty"] = new List<object>(),
            };

            var map = Assert.IsType<Dictionary<string, object>>(ErlangExternalTerm.Decode(ErlangExternalTerm.Encode(value)));

            Assert.Equal("héllo", map["text"]);
            Assert.Equal(7L, map["small"]);
            Assert.Equal(70000L, map["large"]);
            Assert.Equal(1L << 40, map["huge"]);
            Assert.Equal(-1L << 40, map["negative"]);
            Assert.Equal(false, map["flag"]);
            Assert.Null(map["none"]);
            Assert.Equal("a", ((List<object>)map["list"])[0]);
            Assert.Equal(1L, ((Dictionary<string, object>)((List<object>)map["list"])[1])["b"]);
            Assert.Empty((List<object>)map["empty"]);
        }

        [Fact]
        public void WritesBinariesAsTermToBinaryDoes()
        {
            Assert.Equal(new byte[] { 131, 109, 0, 0, 0, 2, (byte)'h', (byte)'i' }, ErlangExternalTerm.Encode("hi"));
        }
    }

    private static byte[] Binary(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new byte[] { 109, 0, 0, 0, (byte)bytes.Length }.Concat(bytes).ToArray();
    }
}
