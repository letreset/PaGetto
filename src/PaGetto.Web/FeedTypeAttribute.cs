using System;
using PaGetto.Core.Entities;

namespace PaGetto.Web;

/// <summary>
/// Marks a controller as part of one package ecosystem's API. Its actions only answer on feeds of
/// that type; on any other feed they are not found (see <see cref="Middleware.FeedTypeMiddleware"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FeedTypeAttribute : Attribute
{
    public FeedTypeAttribute(FeedType type)
    {
        Type = type;
    }

    public FeedType Type { get; }
}
