using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Authentication;

public class ApiKeyAuthenticationService : IAuthenticationService
{
    private readonly ApiKey[] _apiKeys;

    public ApiKeyAuthenticationService(IOptionsSnapshot<PaGettoOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _apiKeys = options.Value.Authentication?.ApiKeys ?? [];
    }

    public Task<bool> AuthenticateAsync(string apiKey, CancellationToken cancellationToken)
        => Task.FromResult(Authenticate(apiKey));

    private bool Authenticate(string apiKey)
    {
        // No authentication is necessary if there is no required API key.
        if (_apiKeys.Length == 0) return true;

        return _apiKeys.Any(x => x.Key.Equals(apiKey));
    }
}
