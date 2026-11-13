using System.Linq;
using PaGetto.Core.RubyGems;
using Xunit;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyMarshalWriterTests
{
    public class ToArray
    {
        [Fact]
        public void WritesWhatRubyMarshalDumpWrites()
        {
            // Marshal.dump([Gem::Version.new("1.0"), "1.0", 4, 300, nil, true, false, :runtime, :runtime])
            var writer = new RubyMarshalWriter();
            writer.WriteArrayStart(9);
            writer.WriteUserMarshalStart("Gem::Version");
            writer.WriteArrayStart(1);
            writer.WriteString("1.0");
            writer.WriteString("1.0");
            writer.WriteInteger(4);
            writer.WriteInteger(300);
            writer.WriteNil();
            writer.WriteBoolean(true);
            writer.WriteBoolean(false);
            writer.WriteSymbol("runtime");
            writer.WriteSymbol("runtime");

            Assert.Equal(
                "04 08 5B 0E 55 3A 11 47 65 6D 3A 3A 56 65 72 73 69 6F 6E 5B 06 49 22 08 31 2E 30 06 3A 06 45 54 "
                + "49 22 08 31 2E 30 06 3B 06 54 69 09 69 02 2C 01 30 54 46 3A 0C 72 75 6E 74 69 6D 65 3B 07",
                string.Join(' ', writer.ToArray().Select(b => b.ToString("X2"))));
        }
    }
}
