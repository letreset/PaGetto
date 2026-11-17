using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PaGetto.Core.Cran;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranDcfTests
{
    public class ReadRecords
    {
        [Fact]
        public void ReadsRecordsSeparatedByBlankLines()
        {
            var text = "Package: a1\nVersion: 1.0\n\n  \nPackage: b2\nVersion: 2.0\nDepends: R (>= 4.0),\n    a1\n";

            var records = CranDcf.ReadRecords(new StringReader(text)).ToList();

            Assert.Equal(2, records.Count);
            Assert.Equal("a1", CranDcf.GetValue(records[0], "Package"));
            Assert.Equal("2.0", CranDcf.GetValue(records[1], "Version"));
            Assert.Equal("R (>= 4.0),\na1", CranDcf.GetValue(records[1], "Depends"));
        }

        [Fact]
        public void KeepsTheOrderOfTheFieldsAndTheFirstOfTwoWithOneName()
        {
            var record = CranDcf.ReadRecord("Version: 1.0\nPackage: a1\nVersion: 2.0\nEmpty:\n");

            Assert.Equal(new[] { "Version", "Package", "Empty" }, record.Select(f => f.Key));
            Assert.Equal("1.0", CranDcf.GetValue(record, "Version"));
            Assert.Equal("", CranDcf.GetValue(record, "Empty"));
        }

        [Theory]
        [InlineData("  continued\nPackage: a1\n", "continues a field")]
        [InlineData("Package a1\n", "not a field")]
        [InlineData("\n\n", "no fields")]
        [InlineData("Package: a1\n\nPackage: b2\n", "more than one record")]
        public void RejectsFilesThatAreNotOneRecord(string text, string message)
        {
            var exception = Assert.Throws<InvalidDataException>(() => CranDcf.ReadRecord(text));

            Assert.Contains(message, exception.Message);
        }
    }

    public class WriteRecord
    {
        [Fact]
        public void WritesEachValueOnOneLineAndLeavesOutEmptyOnes()
        {
            var output = new StringBuilder();

            CranDcf.WriteRecord(output, [
                KeyValuePair.Create("Package", "a1"),
                KeyValuePair.Create("Depends", "R (>= 4.0),\n  a1"),
                KeyValuePair.Create("Suggests", " "),
            ]);

            Assert.Equal("Package: a1\nDepends: R (>= 4.0), a1\n\n", output.ToString());
        }
    }
}
