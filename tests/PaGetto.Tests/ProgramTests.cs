using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Xunit;

namespace PaGetto.Tests;

public class ProgramTests
{
    public class AddPlatformConfigFile : FactsBase
    {
        [Fact]
        public void OverridesAppFolderAppSettings()
        {
            WriteAppSettings("""{ "Key": "app" }""");
            WritePlatformFile("""{ "Key": "platform" }""");

            var config = Build();

            Assert.Equal("platform", config["Key"]);
        }

        [Fact]
        public void IsOverriddenByEnvironmentVariables()
        {
            WritePlatformFile("""{ "Key": "platform" }""");
            Environment.SetEnvironmentVariable(EnvPrefix + "Key", "env");

            var config = Build();

            Assert.Equal("env", config["Key"]);
        }

        [Fact]
        public void IsOptional()
        {
            WriteAppSettings("""{ "Key": "app" }""");

            var config = Build();

            Assert.Equal("app", config["Key"]);
        }

        [Fact]
        public void WatchesFileWhenFolderExists()
        {
            WritePlatformFile("{}");

            Assert.True(AddSource().ReloadOnChange);
        }

        [Fact]
        public void DoesNotWatchWhenFolderIsMissing()
        {
            Assert.False(AddSource().ReloadOnChange);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly string EnvPrefix = $"PAGETTO_TEST_{Guid.NewGuid():N}_";

        private readonly string _appFolder;
        private readonly string _platformFolder;
        private ConfigurationRoot _config;

        protected FactsBase()
        {
            _appFolder = Directory.CreateTempSubdirectory("pagetto-app-").FullName;
            _platformFolder = Path.Combine(Path.GetTempPath(), "pagetto-platform-" + Guid.NewGuid().ToString("N"));
        }

        protected void WriteAppSettings(string json)
        {
            File.WriteAllText(Path.Combine(_appFolder, "appsettings.json"), json);
        }

        protected void WritePlatformFile(string json)
        {
            Directory.CreateDirectory(_platformFolder);
            File.WriteAllText(Path.Combine(_platformFolder, "appsettings.json"), json);
        }

        /// <summary>
        /// Mirrors the order <c>Host.CreateDefaultBuilder</c> uses: app folder JSON, then
        /// environment variables.
        /// </summary>
        protected IConfigurationRoot Build()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(_appFolder)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables(EnvPrefix);

            Program.AddPlatformConfigFile(builder, Path.Combine(_platformFolder, "appsettings.json"));

            _config = (ConfigurationRoot)builder.Build();
            return _config;
        }

        protected JsonConfigurationSource AddSource()
        {
            var builder = new ConfigurationBuilder();
            Program.AddPlatformConfigFile(builder, Path.Combine(_platformFolder, "appsettings.json"));

            return Assert.IsType<JsonConfigurationSource>(Assert.Single(builder.Sources));
        }

        public void Dispose()
        {
            _config?.Dispose();
            Environment.SetEnvironmentVariable(EnvPrefix + "Key", null);
            Directory.Delete(_appFolder, recursive: true);
            if (Directory.Exists(_platformFolder))
            {
                Directory.Delete(_platformFolder, recursive: true);
            }
        }
    }
}
