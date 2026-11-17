using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// The names of pods. A pod name is case-sensitive: CocoaPods finds a pod's files under a prefix of
/// the MD5 hash of its exact name. So a pod is stored under its escaped name (see <see cref="Escape"/>),
/// which keeps the case, is unique regardless of the database's collation, and survives the
/// lowercasing of URLs by routing.
/// </summary>
public static partial class CocoaPodsPodName
{
    /// <summary>
    /// The longest name accepted. Its escaped form fits the database column even when every
    /// letter is uppercase.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The lengths of the hash prefixes that shard the repository (the <c>prefix_lengths</c> of
    /// <c>CocoaPods-version.yml</c>), as on cdn.cocoapods.org.
    /// </summary>
    private static readonly int[] _prefixLengths = [1, 1, 1];

    /// <summary>
    /// Whether <paramref name="name"/> is a pod name this server stores: ASCII letters, digits and
    /// <c>_ + - . ! @ ( )</c>, not starting with a dot. CocoaPods itself only forbids white space,
    /// slashes and a leading dot; the few pods of the public repository with other characters can't
    /// be mirrored.
    /// </summary>
    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && NameRegex().IsMatch(name);
    }

    /// <summary>
    /// Escapes a name for storage: each uppercase letter becomes <c>!</c> and the lowercase letter,
    /// and <c>!</c> becomes <c>!!</c>, e.g. <c>!my!lib</c> for <c>MyLib</c>.
    /// </summary>
    public static string Escape(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        foreach (var c in name)
        {
            if (c is >= 'A' and <= 'Z')
            {
                builder.Append('!').Append(char.ToLowerInvariant(c));
            }
            else if (c == '!')
            {
                builder.Append("!!");
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reverses <see cref="Escape"/>. An escaped name has no uppercase letters, and each <c>!</c> is
    /// followed by a lowercase letter or another <c>!</c>.
    /// </summary>
    public static bool TryUnescape(string escaped, out string name)
    {
        name = null;
        if (string.IsNullOrEmpty(escaped))
        {
            return false;
        }

        var builder = new StringBuilder(escaped.Length);
        for (var i = 0; i < escaped.Length; i++)
        {
            var c = escaped[i];
            if (c is >= 'A' and <= 'Z')
            {
                return false;
            }

            if (c != '!')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 >= escaped.Length)
            {
                return false;
            }

            var next = escaped[++i];
            if (next == '!')
            {
                builder.Append('!');
            }
            else if (next is >= 'a' and <= 'z')
            {
                builder.Append(char.ToUpperInvariant(next));
            }
            else
            {
                return false;
            }
        }

        name = builder.ToString();
        return true;
    }

    /// <summary>
    /// The shard of a pod: the prefixes of the hex MD5 of its name, e.g. <c>["d", "a", "2"]</c> for
    /// <c>Alamofire</c>. A pod's podspecs are under <c>Specs/d/a/2/Alamofire/</c>, and its versions
    /// are listed in <c>all_pods_versions_d_a_2.txt</c>.
    /// </summary>
    public static string[] GetShard(string name)
    {
#pragma warning disable CA5351 // MD5 is how CocoaPods shards its repository, not for security.
        var hash = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(name)));
#pragma warning restore CA5351

        var shard = new string[_prefixLengths.Length];
        var start = 0;
        for (var i = 0; i < _prefixLengths.Length; i++)
        {
            shard[i] = hash.Substring(start, _prefixLengths[i]);
            start += _prefixLengths[i];
        }

        return shard;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_+!@()-][A-Za-z0-9_+.!@()-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();
}
