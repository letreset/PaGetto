namespace PaGetto.Core.Authentication;

/// <summary>
/// Password rules for local accounts, shared by Admin &gt; Accounts and the change-password page.
/// </summary>
public static class PasswordPolicy
{
    public const int MinPasswordLength = 12;
}
