using System;
using PaGetto.Core.Configuration;
using PaGetto.Database.MySql;
using Microsoft.EntityFrameworkCore;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using Xunit;

namespace PaGetto.Tests;

public class MySqlServerVersionResolverTests
{
    public class Resolve
    {
        // The detection cache is static, so every test uses its own connection string.
        private static string UniqueConnectionString()
        {
            return $"Server=localhost;Database=test_{Guid.NewGuid():N};";
        }

        [Fact]
        public void UsesConfiguredServerVersionWithoutDetecting()
        {
            var options = new DatabaseOptions
            {
                ConnectionString = UniqueConnectionString(),
                ServerVersion = "8.0.36-mysql",
            };

            var version = MySqlServerVersionResolver.Resolve(
                options,
                _ => throw new InvalidOperationException("AutoDetect must not be called"));

            Assert.Equal(ServerType.MySql, version.Type);
            Assert.Equal(new Version(8, 0, 36), version.Version);
        }

        [Fact]
        public void ParsesMariaDbServerVersion()
        {
            var options = new DatabaseOptions
            {
                ConnectionString = UniqueConnectionString(),
                ServerVersion = "11.4.2-mariadb",
            };

            var version = MySqlServerVersionResolver.Resolve(
                options,
                _ => throw new InvalidOperationException("AutoDetect must not be called"));

            Assert.Equal(ServerType.MariaDb, version.Type);
        }

        [Fact]
        public void DetectsOncePerConnectionString()
        {
            var options = new DatabaseOptions { ConnectionString = UniqueConnectionString() };
            var calls = 0;
            ServerVersion Detect(string _)
            {
                calls++;
                return new MySqlServerVersion(new Version(8, 0, 36));
            }

            var first = MySqlServerVersionResolver.Resolve(options, Detect);
            var second = MySqlServerVersionResolver.Resolve(options, Detect);

            Assert.Equal(1, calls);
            Assert.Same(first, second);
        }

        [Fact]
        public void DetectsSeparatelyForDifferentConnectionStrings()
        {
            var calls = 0;
            ServerVersion Detect(string _)
            {
                calls++;
                return new MySqlServerVersion(new Version(8, 0, 36));
            }

            MySqlServerVersionResolver.Resolve(new DatabaseOptions { ConnectionString = UniqueConnectionString() }, Detect);
            MySqlServerVersionResolver.Resolve(new DatabaseOptions { ConnectionString = UniqueConnectionString() }, Detect);

            Assert.Equal(2, calls);
        }

        [Fact]
        public void RetriesDetectionAfterFailure()
        {
            var options = new DatabaseOptions { ConnectionString = UniqueConnectionString() };
            var calls = 0;
            ServerVersion Detect(string _)
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("Server unreachable");
                return new MySqlServerVersion(new Version(8, 0, 36));
            }

            Assert.Throws<InvalidOperationException>(() => MySqlServerVersionResolver.Resolve(options, Detect));
            var version = MySqlServerVersionResolver.Resolve(options, Detect);

            Assert.Equal(2, calls);
            Assert.Equal(new Version(8, 0, 36), version.Version);
        }
    }
}
