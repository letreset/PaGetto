using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PaGetto.Core.Entities;

namespace PaGetto.Web.Models;

public class CreateFeedRequest
{
    [BindRequired]
    public string Slug { get; set; }

    [BindRequired]
    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>
    /// The package ecosystem the feed serves: <c>"NuGet"</c>, <c>"Pub"</c>, <c>"Npm"</c> or <c>"Maven"</c>. NuGet when omitted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FeedType>))]
    public FeedType Type { get; set; }
}
