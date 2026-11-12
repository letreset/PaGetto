using System.Collections.Generic;

namespace PaGetto.Core.Npm;

/// <summary>
/// The outcome of a change to an npm package through <see cref="INpmPackageService"/>.
/// </summary>
public sealed class NpmResult
{
    public NpmStatus Status { get; init; }

    /// <summary>
    /// The version the change was about, when there is one.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the change was refused, for the person making it. Null on success.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// The versions the change removed from the feed, so each removal can be audited.
    /// </summary>
    public IReadOnlyList<string> Unpublished { get; init; } = [];

    public static NpmResult Success(string version = null)
    {
        return new NpmResult { Status = NpmStatus.Success, Version = version };
    }

    public static NpmResult Failure(NpmStatus status, string message, string version = null)
    {
        return new NpmResult { Status = status, Message = message, Version = version };
    }
}

public enum NpmStatus
{
    /// <summary>
    /// The change was made.
    /// </summary>
    Success,

    /// <summary>
    /// The package, version or tag does not exist in the feed.
    /// </summary>
    NotFound,

    /// <summary>
    /// The request is not valid, e.g. the tarball does not match its manifest.
    /// </summary>
    Invalid,

    /// <summary>
    /// The feed already has this version and does not allow overwriting it.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// The tarball is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The change needs a permission the caller does not have, e.g. removing versions without the
    /// delete permission.
    /// </summary>
    Forbidden,
}
