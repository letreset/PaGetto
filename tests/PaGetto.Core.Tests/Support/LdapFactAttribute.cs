using System;
using Xunit;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// A test that needs the test LDAP server (<c>ghcr.io/rroemhild/docker-test-openldap</c>), whose
/// <c>host:port</c> is in the <c>PAGETTO_TEST_LDAP</c> environment variable. Skipped without it.
/// </summary>
public sealed class LdapFactAttribute : FactAttribute
{
    public const string ServerVariable = "PAGETTO_TEST_LDAP";

    public LdapFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ServerVariable)))
            Skip = $"Set {ServerVariable} to the host:port of the test LDAP server to run this test.";
    }
}
