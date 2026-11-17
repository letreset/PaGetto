using System.Linq;
using PaGetto.Core.Cran;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranManifestTests
{
    public class Create
    {
        [Fact]
        public void KeepsTheFieldsInTheirOrderWithTheComputedMd5()
        {
            var description = CranDcf.ReadRecord(CranPackageBuilder.Description("Hello", "1.0") + "MD5sum: wrong\n");

            var fields = CranManifest.Parse(CranManifest.Create(description, "0123abcd"));

            Assert.Equal(description.Select(f => f.Key), fields.Select(f => f.Key));
            Assert.Equal("0123abcd", CranDcf.GetValue(fields, "MD5sum"));
            Assert.Equal("Hello", CranManifest.GetName(CranManifest.Create(description, "0123abcd")));
        }
    }

    public class ToIndexEntry
    {
        [Fact]
        public void HasTheFieldsOfPackagesInTheirOrder()
        {
            var manifest = CranManifest.Create(CranDcf.ReadRecord(CranPackageBuilder.Description("hello", "1.0")), "0123abcd");

            var entry = CranManifest.ToIndexEntry(CranManifest.Parse(manifest)).ToList();

            Assert.Equal(new[] { "Package", "Version", "Depends", "Imports", "License", "MD5sum", "NeedsCompilation" }, entry.Select(f => f.Key));
            Assert.Equal("R (>= 4.1.0)", CranDcf.GetValue(entry, "Depends"));
        }
    }
}
