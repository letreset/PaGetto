using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaGetto.Core.Pub;
using Xunit;

namespace PaGetto.Core.Tests.Pub;

public class PubspecYamlTests
{
    public class ToJson
    {
        [Fact]
        public void ConvertsAPubspec()
        {
            var json = PubspecYaml.ToJson("""
                name: my_package
                version: 1.2.3+4
                description: >-
                  A folded
                  description.
                publish_to: none
                environment:
                  sdk: '>=3.0.0 <4.0.0'
                dependencies:
                  http: ^1.0.0
                  flutter:
                    sdk: flutter
                  anything:
                flutter:
                  uses-material-design: true
                topics:
                  - network
                  - http
                """);

            Assert.Equal("my_package", (string)json["name"]);
            Assert.Equal("1.2.3+4", (string)json["version"]);
            Assert.Equal("A folded description.", (string)json["description"]);
            Assert.Equal("none", (string)json["publish_to"]);
            Assert.Equal(">=3.0.0 <4.0.0", (string)json["environment"]["sdk"]);
            Assert.Equal("^1.0.0", (string)json["dependencies"]["http"]);
            Assert.Equal("flutter", (string)json["dependencies"]["flutter"]["sdk"]);
            Assert.Null(json["dependencies"]["anything"]);
            Assert.True((bool)json["flutter"]["uses-material-design"]);
            Assert.Equal(2, ((JsonArray)json["topics"]).Count);
        }

        [Fact]
        public void ResolvesPlainScalarsLikeYaml()
        {
            var json = PubspecYaml.ToJson("""
                integer: 42
                float: 1.5
                quoted: '42'
                boolean: false
                nothing: ~
                text: 1.2.3
                """);

            Assert.Equal(JsonValueKind.Number, json["integer"].GetValueKind());
            Assert.Equal(42, (long)json["integer"]);
            Assert.Equal(1.5, (double)json["float"]);
            Assert.Equal("42", (string)json["quoted"]);
            Assert.False((bool)json["boolean"]);
            Assert.Null(json["nothing"]);
            Assert.Equal("1.2.3", (string)json["text"]);
        }

        [Theory]
        [InlineData("- a\n- b")]
        [InlineData("just text")]
        [InlineData("")]
        [InlineData("name: [unclosed")]
        public void RejectsWhatIsNotAMapping(string yaml)
        {
            Assert.Throws<InvalidPubArchiveException>(() => PubspecYaml.ToJson(yaml));
        }

        [Fact]
        public void RejectsAliasesThatExpandWithoutBound()
        {
            // Every level repeats the previous one nine times.
            var yaml = new StringBuilder("a0: &a0 [x, x, x, x, x, x, x, x, x]\n");
            for (var i = 1; i <= 8; i++)
            {
                yaml.Append($"a{i}: &a{i} [*a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}]\n");
            }

            Assert.Throws<InvalidPubArchiveException>(() => PubspecYaml.ToJson(yaml.ToString()));
        }
    }
}
