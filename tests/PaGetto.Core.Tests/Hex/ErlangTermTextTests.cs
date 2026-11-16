using System;
using System.Collections.Generic;
using System.Text;
using PaGetto.Core.Hex;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class ErlangTermTextTests
{
    public class Parse
    {
        [Fact]
        public void ReadsTheTermsOfAMetadataFile()
        {
            var terms = Read("""
                {<<"name">>,<<"demo">>}.
                {<<"licenses">>,[<<"MIT">>,<<"Apache-2.0">>]}.
                {<<"requirements">>,
                 [[{<<"name">>,<<"jason">>},
                   {<<"optional">>,false},
                   {<<"requirement">>,<<"~> 1.4">>}]]}.
                """);

            Assert.Equal(3, terms.Count);
            Assert.Equal(new object[] { "name", "demo" }, (object[])terms[0]);
            Assert.Equal(new List<object> { "MIT", "Apache-2.0" }, ((object[])terms[1])[1]);

            var requirement = (List<object>)((List<object>)((object[])terms[2])[1])[0];
            Assert.Equal(new object[] { "optional", false }, (object[])requirement[1]);
        }

        [Fact]
        public void ReadsUtf8BinariesAndEscapes()
        {
            var terms = Read("{<<\"description\">>,<<\"Café \\\"quoted\\\"\\n\\101\"/utf8>>}.");

            Assert.Equal("Café \"quoted\"\nA", ((object[])terms[0])[1]);
        }

        [Fact]
        public void ReadsBinariesOfBytes()
        {
            var terms = Read("{<<\"a\">>,<<104,105>>}. {<<\"b\">>,<<>>}.");

            Assert.Equal("hi", ((object[])terms[0])[1]);
            Assert.Equal(string.Empty, ((object[])terms[1])[1]);
        }

        [Fact]
        public void ReadsIntegersAtomsAndStrings()
        {
            var terms = Read("{<<\"a\">>,-12}. {<<\"b\">>,nil}. {<<\"c\">>,'quoted atom'}. {<<\"d\">>,\"chars\"}. {<<\"e\">>,{}}.");

            Assert.Equal(-12L, ((object[])terms[0])[1]);
            Assert.Equal("nil", ((object[])terms[1])[1]);
            Assert.Equal("quoted atom", ((object[])terms[2])[1]);
            Assert.Equal("chars", ((object[])terms[3])[1]);
            Assert.Empty((object[])((object[])terms[4])[1]);
        }

        [Fact]
        public void FallsBackToLatin1()
        {
            var terms = ErlangTermText.Parse(Encoding.Latin1.GetBytes("{<<\"a\">>,\"café\"}."));

            Assert.Equal("café", ((object[])terms[0])[1]);
        }

        [Theory]
        [InlineData("{<<\"a\">>,<<\"b\">>}")]
        [InlineData("{<<\"a\">>,<<\"b\">>.")]
        [InlineData("{<<\"a\">>,<<\"b>>}.")]
        [InlineData("{<<\"a\">>,<<256>>}.")]
        [InlineData("{<<\"a\">>,#{}}.")]
        [InlineData("{<<\"a\">>,Var}.")]
        public void RejectsWhatHexDoesNotWrite(string text)
        {
            Assert.Throws<FormatException>(() => Read(text));
        }

        [Fact]
        public void RejectsDeepNesting()
        {
            Assert.Throws<FormatException>(() => Read(new string('[', 100) + new string(']', 100) + "."));
        }

        private static IReadOnlyList<object> Read(string text)
        {
            return ErlangTermText.Parse(Encoding.UTF8.GetBytes(text));
        }
    }
}
