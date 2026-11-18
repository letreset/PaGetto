using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

public class MetricsIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public MetricsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MetricsAreExportedWhenAnOtlpEndpointIsSet()
    {
        using var app = new PaGettoApplication(_output, null, config => config["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317");

        Assert.NotNull(app.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void MetricsAreOffWithoutAnOtlpEndpoint()
    {
        using var app = new PaGettoApplication(_output);

        Assert.Null(app.Services.GetService<MeterProvider>());
    }
}
