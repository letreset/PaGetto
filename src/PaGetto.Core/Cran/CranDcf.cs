using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Cran;

/// <summary>
/// The Debian Control File format of R's <c>DESCRIPTION</c> and <c>PACKAGES</c> files, as
/// <c>read.dcf</c> reads it: records separated by blank lines, each a list of <c>Field: value</c>
/// lines, where a line that starts with white space continues the value of the field before it.
/// See: https://cran.r-project.org/doc/manuals/r-release/R-exts.html#The-DESCRIPTION-file
/// </summary>
public static partial class CranDcf
{
    /// <summary>
    /// Reads the records of a file. Within a value, continuation lines are joined with a newline,
    /// without their leading white space. The first of two fields with the same name is kept.
    /// </summary>
    /// <param name="reader">The file.</param>
    /// <returns>The records, each with its fields in the order of the file.</returns>
    /// <exception cref="InvalidDataException">A line is neither a field nor a continuation line.</exception>
    public static IEnumerable<IReadOnlyList<KeyValuePair<string, string>>> ReadRecords(TextReader reader)
    {
        var fields = new List<KeyValuePair<string, string>>();
        string name = null;
        StringBuilder value = null;

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddField(fields, ref name, ref value);
                if (fields.Count > 0)
                {
                    yield return fields;
                    fields = new List<KeyValuePair<string, string>>();
                }

                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                if (value == null)
                {
                    throw new InvalidDataException($"The line '{Truncate(line)}' continues a field, but no field comes before it.");
                }

                value.Append('\n').Append(line.Trim());
                continue;
            }

            var match = FieldRegex().Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException($"The line '{Truncate(line)}' is not a field of the form 'Name: value'.");
            }

            AddField(fields, ref name, ref value);
            name = match.Groups[1].Value;
            value = new StringBuilder(match.Groups[2].Value.Trim());
        }

        AddField(fields, ref name, ref value);
        if (fields.Count > 0)
        {
            yield return fields;
        }
    }

    /// <summary>
    /// Reads the one record of a file such as <c>DESCRIPTION</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The file has no record, more than one, or a line that is not valid.</exception>
    public static IReadOnlyList<KeyValuePair<string, string>> ReadRecord(string text)
    {
        using var reader = new StringReader(text);
        IReadOnlyList<KeyValuePair<string, string>> record = null;

        foreach (var fields in ReadRecords(reader))
        {
            if (record != null)
            {
                throw new InvalidDataException("The file has more than one record; records are separated by blank lines.");
            }

            record = fields;
        }

        return record ?? throw new InvalidDataException("The file has no fields.");
    }

    /// <summary>
    /// Writes a record, followed by the blank line that ends it. Each value is written on one line,
    /// with its runs of white space (newlines included) as single spaces; empty values are left out.
    /// </summary>
    public static void WriteRecord(StringBuilder output, IEnumerable<KeyValuePair<string, string>> fields)
    {
        foreach (var (name, value) in fields)
        {
            var oneLine = value == null ? "" : WhiteSpaceRegex().Replace(value, " ").Trim();
            if (oneLine.Length > 0)
            {
                output.Append(name).Append(": ").Append(oneLine).Append('\n');
            }
        }

        output.Append('\n');
    }

    /// <summary>
    /// The value of a field, or null if the record has no such field.
    /// </summary>
    public static string GetValue(IReadOnlyList<KeyValuePair<string, string>> fields, string name)
    {
        foreach (var (field, value) in fields)
        {
            if (string.Equals(field, name, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    private static void AddField(List<KeyValuePair<string, string>> fields, ref string name, ref StringBuilder value)
    {
        if (name != null && GetValue(fields, name) == null)
        {
            fields.Add(KeyValuePair.Create(name, value!.ToString()));
        }

        name = null;
        value = null;
    }

    private static string Truncate(string line)
    {
        return line.Length > 80 ? line[..80] + "..." : line;
    }

    // A field name is printable ASCII without a colon or white space.
    [GeneratedRegex(@"^([!-9;-~]+):(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpaceRegex();
}
