using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PaGetto.Web.Models;

public class UpdateFeedRequest
{
    [BindRequired]
    public string Name { get; set; }

    public string Description { get; set; }
}
