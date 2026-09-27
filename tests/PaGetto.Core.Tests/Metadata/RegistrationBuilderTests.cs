using System;
using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Metadata;
using PaGetto.Core.Tests.Support;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Core.Tests.Metadata;

public class RegistrationBuilderTests
{
    private readonly Mock<IUrlGenerator> _urlGenerator;
    private readonly RegistrationBuilder _registrationBuilder;

    public RegistrationBuilderTests()
    {
        _urlGenerator = new Mock<IUrlGenerator>();
        _registrationBuilder = new RegistrationBuilder(_urlGenerator.Object, Options.Create(new PaGettoOptions()));
    }

    #region helper methods

    private PackageRegistration GetPackageRegistration()
    {
        var packageId = "PaGetto.Test";
        var packages = new List<Package>
        {
            Generator.GetPackage(packageId, "3.1.0"),
            Generator.GetPackage(packageId, "10.0.5", downloads:42),
            Generator.GetPackage(packageId, "3.2.0"),
            Generator.GetPackage(packageId, "3.1.0-pre"),
            Generator.GetPackage(packageId, "1.0.0-beta1", downloads:21),
            Generator.GetPackage(packageId, "1.0.0"),
        };

        return new PackageRegistration(packageId, packages);
    }

    #endregion

    [Fact]
    public void Ctor_UrlGeneratorIsNull_ShouldThrow()
    {
        // Act/Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new RegistrationBuilder(null, Options.Create(new PaGettoOptions())));
    }

    [Fact]
    public void BuildIndex_PackageRegistrationIsNull_ShouldThrow()
    {
        // Arrange
        var registrationBuilder = new RegistrationBuilder(_urlGenerator.Object, Options.Create(new PaGettoOptions()));

        // Act/Assert
        var ex = Assert.Throws<ArgumentNullException>(() => registrationBuilder.BuildIndex(null));
    }

    [Fact]
    public void BuildIndex_RegistrationIndexResponse_ShouldBeSortedByVersion()
    {
        // Arrange
        var registration = GetPackageRegistration();

        // Act
        var response = _registrationBuilder.BuildIndex(registration);

        // Assert
        Assert.Equal(registration.Packages.Count, response.Pages[0].ItemsOrNull.Count);

        var index = 0;
        foreach (var package in registration.Packages.OrderBy(p => p.Version))
        {
            Assert.Equal(package.Version.ToFullString(), response.Pages[0].ItemsOrNull[index++].PackageMetadata.Version);
        }
    }

    [Fact]
    public void BuildIndex_RegistrationIndexResponse_ShouldHaveCorrectTotalDownloads()
    {
        // Arrange
        var registration = GetPackageRegistration();

        // Act
        var response = _registrationBuilder.BuildIndex(registration);

        // Assert
        Assert.Equal(registration.Packages.Sum(x => x.Downloads), response.TotalDownloads);
    }

    [Fact]
    public void Ctor_OptionsIsNull_ShouldThrow()
    {
        // Act/Assert
        Assert.Throws<ArgumentNullException>(() => new RegistrationBuilder(_urlGenerator.Object, null));
    }

    [Fact]
    public void BuildIndex_WithinPageSize_ShouldInlineItemsInSinglePage()
    {
        // Arrange
        var registration = GetPackageRegistration();
        _urlGenerator.Setup(x => x.GetRegistrationIndexUrl(It.IsAny<string>())).Returns("https://example.test/index.json");

        // Act
        var response = _registrationBuilder.BuildIndex(registration);

        // Assert
        Assert.Equal(1, response.Count);
        Assert.Single(response.Pages);
        Assert.Equal("https://example.test/index.json", response.Pages[0].RegistrationPageUrl);
        _urlGenerator.Verify(x => x.GetRegistrationPageUrl(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<NuGetVersion>()), Times.Never);
    }

    [Fact]
    public void BuildIndex_WhenPageSizeExceeded_ShouldReturnPagesWithoutInlinedItems()
    {
        // Arrange
        var registrationBuilder = new RegistrationBuilder(_urlGenerator.Object, Options.Create(new PaGettoOptions { RegistrationPageSize = 4 }));
        var registration = GetPackageRegistration();
        _urlGenerator.Setup(x => x.GetRegistrationPageUrl(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<NuGetVersion>()))
            .Returns<string, NuGetVersion, NuGetVersion>((id, lower, upper) => $"https://example.test/{id}/page/{lower.ToNormalizedString()}/{upper.ToNormalizedString()}.json");

        // Act
        var response = registrationBuilder.BuildIndex(registration);

        // Assert
        Assert.Equal(2, response.Count);
        Assert.All(response.Pages, page => Assert.Null(page.ItemsOrNull));

        Assert.Equal(4, response.Pages[0].Count);
        Assert.Equal("1.0.0-beta1", response.Pages[0].Lower);
        Assert.Equal("3.1.0", response.Pages[0].Upper);
        Assert.Equal("https://example.test/PaGetto.Test/page/1.0.0-beta1/3.1.0.json", response.Pages[0].RegistrationPageUrl);

        Assert.Equal(2, response.Pages[1].Count);
        Assert.Equal("3.2.0", response.Pages[1].Lower);
        Assert.Equal("10.0.5", response.Pages[1].Upper);
    }

    [Fact]
    public void BuildPage_WhenRangeMatchesPackages_ShouldReturnSortedInlinedItems()
    {
        // Arrange
        var registration = GetPackageRegistration();

        // Act
        var response = _registrationBuilder.BuildPage(registration, NuGetVersion.Parse("3.1.0-pre"), NuGetVersion.Parse("3.2.0"));

        // Assert
        Assert.NotNull(response);
        Assert.Equal(3, response.Count);
        Assert.Equal("3.1.0-pre", response.Lower);
        Assert.Equal("3.2.0", response.Upper);
        Assert.Equal(
            new[] { "3.1.0-pre", "3.1.0", "3.2.0" },
            response.ItemsOrNull.Select(i => i.PackageMetadata.Version));
    }

    [Fact]
    public void BuildPage_WhenRangeMatchesNoPackages_ShouldReturnNull()
    {
        // Arrange
        var registration = GetPackageRegistration();

        // Act
        var response = _registrationBuilder.BuildPage(registration, NuGetVersion.Parse("4.0.0"), NuGetVersion.Parse("5.0.0"));

        // Assert
        Assert.Null(response);
    }

    [Fact]
    public void BuildLeaf_RegistrationLeafResponse_ShouldHaveCorrectProperties()
    {
        // Arrange
        var packageId = "dummy";
        var packageVersion = "1.0.42";
        var isPackageListed = true;
        var publishDate = DateTime.UtcNow;

        var package = Generator.GetPackage(packageId, packageVersion);
        package.Listed = isPackageListed;
        package.Published = publishDate;

        // Act
        var response = _registrationBuilder.BuildLeaf(package);

        // Assert
        Assert.Equal(isPackageListed, response.Listed);
        Assert.Equal(publishDate, response.Published);
    }
}
