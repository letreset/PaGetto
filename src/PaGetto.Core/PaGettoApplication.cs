using System;
using Microsoft.Extensions.DependencyInjection;

namespace PaGetto.Core;

public class PaGettoApplication
{
    public PaGettoApplication(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}
