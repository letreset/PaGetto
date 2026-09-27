using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Tests.Support;

/// <summary>
/// Routes each request to the in-memory server registered for its host, so a downstream app can
/// mirror several upstream test servers. Unknown hosts get 503, like an unreachable upstream.
/// </summary>
public class HostRoutingHandler : HttpMessageHandler
{
    private readonly Dictionary<string, HttpMessageInvoker> _routes = new(StringComparer.OrdinalIgnoreCase);

    public HostRoutingHandler Route(string host, HttpMessageHandler handler)
    {
        _routes[host] = new HttpMessageInvoker(handler);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_routes.TryGetValue(request.RequestUri.Host, out var invoker))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        return invoker.SendAsync(request, cancellationToken);
    }
}
