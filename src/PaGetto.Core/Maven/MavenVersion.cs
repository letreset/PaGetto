using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace PaGetto.Core.Maven;

/// <summary>
/// A Maven version, ordered the way Maven orders versions (its <c>ComparableVersion</c>): numbers
/// numerically, the qualifiers <c>alpha</c> &lt; <c>beta</c> &lt; <c>milestone</c> &lt; <c>rc</c>
/// &lt; <c>snapshot</c> &lt; release &lt; <c>sp</c> &lt; any other qualifier, and trailing zeros
/// ignored, so 1 = 1.0 = 1.0.0 = 1-ga. Any string is a version.
/// See: https://maven.apache.org/pom.html#version-order-specification
/// </summary>
public sealed class MavenVersion : IComparable<MavenVersion>
{
    private const string SnapshotSuffix = "SNAPSHOT";

    private static readonly string[] Qualifiers = ["alpha", "beta", "milestone", "rc", "snapshot", "", "sp"];

    // The index of the release qualifier "" in Qualifiers, as compared.
    private static readonly string ReleaseQualifier = Array.IndexOf(Qualifiers, string.Empty).ToString(CultureInfo.InvariantCulture);

    private readonly ListItem _items;

    private MavenVersion(string value)
    {
        Value = value;
        _items = ParseItems(value);
        IsPrerelease = HasPrereleaseQualifier(_items);
    }

    /// <summary>
    /// Compares version strings in Maven's order, lowest first.
    /// </summary>
    public static IComparer<string> StringComparer { get; } =
        Comparer<string>.Create((x, y) => Parse(x).CompareTo(Parse(y)));

    /// <summary>
    /// The version as it was given.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Whether the version has a qualifier that orders before the release: alpha, beta, milestone,
    /// rc or snapshot. Other qualifiers, such as the <c>jre</c> of 33.0-jre, are releases.
    /// </summary>
    public bool IsPrerelease { get; }

    public static MavenVersion Parse(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new MavenVersion(version);
    }

    /// <summary>
    /// Whether a version is a snapshot, whose files are replaced by each build: one that ends with
    /// <c>SNAPSHOT</c>, as Maven decides it.
    /// </summary>
    public static bool IsSnapshot(string version)
    {
        return version.EndsWith(SnapshotSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The part of a snapshot version that its timestamped file versions start with, e.g.
    /// <c>1.0-</c> for <c>1.0-SNAPSHOT</c> (whose files are <c>1.0-20240101.120000-1</c>).
    /// </summary>
    public static string GetSnapshotBase(string version)
    {
        return version[..^SnapshotSuffix.Length];
    }

    public int CompareTo(MavenVersion other)
    {
        return other == null ? 1 : _items.CompareTo(other._items);
    }

    public override string ToString()
    {
        return Value;
    }

    private static ListItem ParseItems(string version)
    {
        version = version.ToLowerInvariant();

        var items = new ListItem();
        var list = items;
        var stack = new Stack<ListItem>();
        stack.Push(list);

        var isDigit = false;
        var startIndex = 0;

        for (var i = 0; i < version.Length; i++)
        {
            var c = version[i];

            if (c == '.')
            {
                list.Add(i == startIndex ? IntItem.Zero : ParseItem(isDigit, version[startIndex..i]));
                startIndex = i + 1;
            }
            else if (c == '-')
            {
                list.Add(i == startIndex ? IntItem.Zero : ParseItem(isDigit, version[startIndex..i]));
                startIndex = i + 1;

                var sublist = new ListItem();
                list.Add(sublist);
                list = sublist;
                stack.Push(list);
            }
            else if (char.IsAsciiDigit(c))
            {
                if (!isDigit && i > startIndex)
                {
                    // A letter followed by a digit, e.g. "rc1": the letters are a qualifier.
                    list.Add(new StringItem(version[startIndex..i], followedByDigit: true));
                    startIndex = i;

                    var sublist = new ListItem();
                    list.Add(sublist);
                    list = sublist;
                    stack.Push(list);
                }

                isDigit = true;
            }
            else
            {
                if (isDigit && i > startIndex)
                {
                    list.Add(ParseItem(isDigit: true, version[startIndex..i]));
                    startIndex = i;

                    var sublist = new ListItem();
                    list.Add(sublist);
                    list = sublist;
                    stack.Push(list);
                }

                isDigit = false;
            }
        }

        if (version.Length > startIndex)
        {
            list.Add(ParseItem(isDigit, version[startIndex..]));
        }

        while (stack.Count > 0)
        {
            stack.Pop().Normalize();
        }

        return items;
    }

    private static Item ParseItem(bool isDigit, string text)
    {
        return isDigit
            ? new IntItem(BigInteger.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture))
            : new StringItem(text, followedByDigit: false);
    }

    private static bool HasPrereleaseQualifier(ListItem list)
    {
        return list.Any(item => item switch
        {
            StringItem s => string.CompareOrdinal(s.Comparable, ReleaseQualifier) < 0,
            ListItem l => HasPrereleaseQualifier(l),
            _ => false,
        });
    }

    private abstract class Item
    {
        public abstract bool IsNull { get; }

        /// <param name="other">The item to compare with, or null for a missing item.</param>
        public abstract int CompareTo(Item other);
    }

    private sealed class IntItem : Item
    {
        public static readonly IntItem Zero = new(BigInteger.Zero);

        private readonly BigInteger _value;

        public IntItem(BigInteger value)
        {
            _value = value;
        }

        public override bool IsNull => _value.IsZero;

        public override int CompareTo(Item other)
        {
            return other switch
            {
                null => _value.IsZero ? 0 : 1,
                IntItem i => _value.CompareTo(i._value),

                // 1.1 > 1-sp and 1.1 > 1-1
                _ => 1,
            };
        }
    }

    private sealed class StringItem : Item
    {
        public StringItem(string value, bool followedByDigit)
        {
            if (followedByDigit && value.Length == 1)
            {
                // a1 = alpha-1, b1 = beta-1, m1 = milestone-1
                value = value[0] switch
                {
                    'a' => "alpha",
                    'b' => "beta",
                    'm' => "milestone",
                    _ => value,
                };
            }

            value = value switch
            {
                "ga" or "final" or "release" => string.Empty,
                "cr" => "rc",
                _ => value,
            };

            var index = Array.IndexOf(Qualifiers, value);

            // Unknown qualifiers order after the known ones, and among themselves alphabetically.
            Comparable = index == -1
                ? $"{Qualifiers.Length}-{value}"
                : index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The qualifier in a form that orders as Maven orders qualifiers, by ordinal comparison.
        /// </summary>
        public string Comparable { get; }

        public override bool IsNull => Comparable == ReleaseQualifier;

        public override int CompareTo(Item other)
        {
            return other switch
            {
                // 1-rc < 1, 1-sp > 1
                null => string.CompareOrdinal(Comparable, ReleaseQualifier),
                StringItem s => string.CompareOrdinal(Comparable, s.Comparable),

                // 1.any < 1.1 and 1-any < 1-1
                _ => -1,
            };
        }
    }

    private sealed class ListItem : Item
    {
        private readonly List<Item> _items = [];

        public override bool IsNull => _items.Count == 0;

        public void Add(Item item)
        {
            _items.Add(item);
        }

        public bool Any(Func<Item, bool> predicate)
        {
            return _items.Any(predicate);
        }

        /// <summary>
        /// Removes trailing items that equal nothing (0, the release qualifier, empty lists), so
        /// that 1.0.0 equals 1.
        /// </summary>
        public void Normalize()
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.IsNull)
                {
                    _items.RemoveAt(i);
                }
                else if (item is not ListItem)
                {
                    break;
                }
            }
        }

        public override int CompareTo(Item other)
        {
            switch (other)
            {
                case null:
                    foreach (var item in _items)
                    {
                        var result = item.CompareTo(null);
                        if (result != 0)
                        {
                            return result;
                        }
                    }

                    return 0;

                // 1-1 < 1.0.x
                case IntItem:
                    return -1;

                // 1-1 > 1-sp
                case StringItem:
                    return 1;

                default:
                    var otherItems = ((ListItem)other)._items;
                    for (var i = 0; i < Math.Max(_items.Count, otherItems.Count); i++)
                    {
                        var left = i < _items.Count ? _items[i] : null;
                        var right = i < otherItems.Count ? otherItems[i] : null;

                        var result = left == null
                            ? (right == null ? 0 : -right.CompareTo(null))
                            : left.CompareTo(right);

                        if (result != 0)
                        {
                            return result;
                        }
                    }

                    return 0;
            }
        }
    }
}
