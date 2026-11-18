using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Authentication;

public record TokenCreateResult(PersonalAccessToken Token, string PlaintextToken);

public interface ITokenService
{
    /// <summary>
    /// Creates a token that may do everything its user may do.
    /// </summary>
    Task<TokenCreateResult> CreateTokenAsync(Guid userId, string name, DateTime expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a token limited by <paramref name="permissions"/>.
    /// </summary>
    Task<TokenCreateResult> CreateTokenAsync(Guid userId, string name, DateTime expiresAtUtc, TokenPermissions permissions, CancellationToken cancellationToken);
    Task<PersonalAccessToken> ValidateTokenAsync(string plaintextToken, CancellationToken cancellationToken);
    Task RevokeTokenAsync(Guid tokenId, CancellationToken cancellationToken);
    Task<List<PersonalAccessToken>> GetUserTokensAsync(Guid userId, CancellationToken cancellationToken);
}
