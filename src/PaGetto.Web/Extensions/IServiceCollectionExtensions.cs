using System;
using System.Text.Json.Serialization;
using PaGetto.Core;
using PaGetto.Core.Extensions;
using PaGetto.Web.Audit;
using PaGetto.Web.Controllers;
using PaGetto.Web.Helper;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaGettoWebApplication(
        this IServiceCollection services,
        Action<PaGettoApplication> configureAction)
    {
        services
            .AddRouting(options => options.LowercaseUrls = true)
            .AddControllers()
            .AddApplicationPart(typeof(PackageContentController).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        services.AddRazorPages();

        services.AddHttpContextAccessor();
        services.AddTransient<IUrlGenerator, PaGettoUrlGenerator>();
        services.AddSingleton<WebAuditLog>();

        services.AddSingleton(ApplicationVersionHelper.GetVersion());

        var app = services.AddPaGettoApplication(configureAction);

        return services;
    }
}
