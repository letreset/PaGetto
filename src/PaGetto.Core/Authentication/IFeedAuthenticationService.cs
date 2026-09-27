using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Authentication;

public interface IFeedAuthenticationService
{
    Task<AuthResult> AuthenticateByTokenAsync(string token, CancellationToken cancellationToken);
    Task<AuthResult> AuthenticateByCredentialsAsync(string username, string password, CancellationToken cancellationToken);
}
