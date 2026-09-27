using System;
using System.Collections.Generic;
using PaGetto.Core.Entities;
using PaGetto.Core.Metadata;
using Xunit;

namespace PaGetto.Core.Tests.Metadata;

public class PackageRegistrationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Ctor_PackageIdIsEmptyOrWhiteSpace_ShouldThrow(string packageId)
    {
        // Arrange
        var packages = new List<Package>();

        // Act/Assert
        var ex = Assert.Throws<ArgumentException>(() => new PackageRegistration(packageId, packages));
    }

    [Fact]
    public void Ctor_PackageIdIsNull_ShouldThrow()
    {
        // Arrange
        var packages = new List<Package>();

        // Act/Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new PackageRegistration(null, packages));
    }

    [Fact]
    public void Ctor_PackageListIsNull_ShouldThrow()
    {
        // Arrange
        var packageId = "dummy";

        // Act/Assert
        Assert.Throws<ArgumentNullException>(() => new PackageRegistration(packageId, null));
    }
}
