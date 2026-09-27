using System;

namespace PaGetto.Core.Authentication;

public record AuthResult(bool IsAuthenticated, Guid? UserId, string Username);
