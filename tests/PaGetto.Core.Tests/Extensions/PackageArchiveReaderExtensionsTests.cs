using System;
using System.IO;
using PaGetto.Core.Extensions;
using NuGet.Packaging;
using NuGet.Packaging.Licenses;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Extensions;

public class PackageArchiveReaderExtensionsTests
{
    public class GetPackageMetadata
    {
        [Fact]
        public void ReadsCopyrightAndLicenseExpression()
        {
            var package = Read(builder =>
            {
                builder.Copyright = "Copyright (c) Contoso";
                builder.LicenseMetadata = new LicenseMetadata(
                    LicenseType.Expression,
                    "MIT",
                    NuGetLicenseExpression.Parse("MIT"),
                    warningsAndErrors: null,
                    LicenseMetadata.CurrentVersion);
            });

            Assert.Equal("Copyright (c) Contoso", package.Copyright);
            Assert.Equal("MIT", package.LicenseExpression);
        }

        [Fact]
        public void HasNoLicenseExpressionForALicenseUrl()
        {
            var package = Read(builder => builder.LicenseUrl = new Uri("https://license.test/"));

            Assert.Null(package.LicenseExpression);
            Assert.Equal("https://license.test/", package.LicenseUrlString);
        }

        private static Entities.Package Read(Action<PackageBuilder> configure)
        {
            var builder = new PackageBuilder
            {
                Id = "TestPackage",
                Version = NuGetVersion.Parse("1.0.0"),
                Description = "Description",
            };
            builder.Authors.Add("Author");
            builder.DependencyGroups.Add(new PackageDependencyGroup(NuGet.Frameworks.NuGetFramework.AnyFramework, [new NuGet.Packaging.Core.PackageDependency("Dependency", VersionRange.Parse("1.0.0"))]));
            configure(builder);

            using var stream = new MemoryStream();
            builder.Save(stream);
            stream.Position = 0;

            using var reader = new PackageArchiveReader(stream);
            return reader.GetPackageMetadata();
        }
    }
}
