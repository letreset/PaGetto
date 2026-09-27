using System;
using System.Collections.Generic;

namespace PaGetto.Core.Entities;

public class Group
{
    private string _name;

    public Guid Id { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NormalizedName = NormalizeName(value);
        }
    }

    /// <summary>
    /// <see cref="Name"/> in upper case. Its unique index keeps group names unique regardless of
    /// case on every database. Set together with <see cref="Name"/>.
    /// </summary>
    public string NormalizedName { get; set; }

    public string AppRoleValue { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<UserGroup> UserGroups { get; set; }

    public static string NormalizeName(string name)
    {
        return name?.ToUpperInvariant();
    }
}
