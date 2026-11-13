using System;
using System.Collections.Generic;

namespace PaGetto.Core.Go;

/// <summary>
/// The parts of a <c>go.mod</c> file this server reads: the module path, the Go version, the
/// toolchain and the requirements. Other directives are skipped, and so is anything it can't read,
/// because the go command checks the file itself.
/// See: https://go.dev/ref/mod#go-mod-file
/// </summary>
public sealed class GoModFile
{
    /// <summary>
    /// The path of the <c>module</c> directive, or null.
    /// </summary>
    public string Module { get; private init; }

    /// <summary>
    /// The version of the <c>go</c> directive, e.g. <c>1.22</c>, or null.
    /// </summary>
    public string GoVersion { get; private init; }

    /// <summary>
    /// The toolchain of the <c>toolchain</c> directive, e.g. <c>go1.22.1</c>, or null.
    /// </summary>
    public string Toolchain { get; private init; }

    /// <summary>
    /// The requirements, in the order of the file.
    /// </summary>
    public IReadOnlyList<GoRequirement> Requires { get; private init; }

    public static GoModFile Parse(string text)
    {
        string module = null;
        string goVersion = null;
        string toolchain = null;
        var requires = new List<GoRequirement>();

        // The verb of the block the current line is in, e.g. "require" for require ( ... ).
        string block = null;

        foreach (var rawLine in (text ?? "").Split('\n'))
        {
            var (code, comment) = SplitComment(rawLine);
            var tokens = Tokenize(code);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (block != null)
            {
                if (tokens.Count == 1 && tokens[0] == ")")
                {
                    block = null;
                }
                else
                {
                    Add(block, tokens);
                }

                continue;
            }

            if (tokens.Count == 2 && tokens[1] == "(")
            {
                block = tokens[0];
                continue;
            }

            Add(tokens[0], tokens.GetRange(1, tokens.Count - 1));

            void Add(string verb, List<string> arguments)
            {
                switch (verb)
                {
                    case "module" when arguments.Count == 1:
                        module ??= arguments[0];
                        break;
                    case "go" when arguments.Count == 1:
                        goVersion ??= arguments[0];
                        break;
                    case "toolchain" when arguments.Count == 1:
                        toolchain ??= arguments[0];
                        break;
                    case "require" when arguments.Count == 2:
                        requires.Add(new GoRequirement(arguments[0], arguments[1], IsIndirect(comment)));
                        break;
                }
            }
        }

        return new GoModFile
        {
            Module = module,
            GoVersion = goVersion,
            Toolchain = toolchain,
            Requires = requires,
        };
    }

    /// <summary>
    /// Splits a line at its <c>//</c> comment, which may not start inside a quoted string.
    /// </summary>
    private static (string Code, string Comment) SplitComment(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0')
            {
                if (c == '\\' && quote == '"')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '`')
            {
                quote = c;
            }
            else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                return (line[..i], line[(i + 2)..]);
            }
        }

        return (line, null);
    }

    /// <summary>
    /// The words of a line, with the quotes of quoted strings removed.
    /// </summary>
    private static List<string> Tokenize(string code)
    {
        var tokens = new List<string>();
        foreach (var word in code.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var unquoted = word.Length >= 2 && (word[0] == '"' || word[0] == '`') && word[^1] == word[0]
                ? word[1..^1]
                : word;

            tokens.Add(unquoted);
        }

        return tokens;
    }

    /// <summary>
    /// Whether the comment of a requirement marks it as indirect: <c>// indirect</c>, possibly
    /// followed by <c>;</c> and more.
    /// </summary>
    private static bool IsIndirect(string comment)
    {
        if (comment == null)
        {
            return false;
        }

        var text = comment.Trim();
        return text == "indirect" || text.StartsWith("indirect;", StringComparison.Ordinal);
    }
}
