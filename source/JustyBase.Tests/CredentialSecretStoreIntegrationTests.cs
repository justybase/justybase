using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Credentials;

namespace JustyBase.Tests;

/// <summary>
/// OS store integration tests (Phase B). Each test returns early when its
/// platform/store is unavailable, so the suite stays green everywhere while
/// exercising the real backend (Windows Credential Manager, Secret Service,
/// Keychain) on machines that have it. Test entries use GUID names and are
/// removed in finally blocks.
/// </summary>
public class CredentialSecretStoreIntegrationTests
{
    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [Fact]
    public void Factory_SelectsPlatformBackend()
    {
        ICredentialSecretStore store = CredentialSecretStoreFactory.Create(ISimpleLogger.EmptyLogger);
        Assert.NotNull(store);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsCredentialStore>(store);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacKeychainStore>(store);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxSecretToolStore>(store);
        }
    }

    [Fact]
    public void UnavailableStore_ReportsFalseEverywhere()
    {
        ICredentialSecretStore store = new UnavailableSecretStore();
        Assert.False(store.IsAvailable);
        Assert.False(store.TryGetSecret("X", out string? secret));
        Assert.Null(secret);
        Assert.False(store.TrySetSecret("X", "y"));
        Assert.False(store.TryRemoveSecret("X"));
    }

    [Fact]
    public void WindowsCredentialStore_RoundTrips_WhenAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore(ISimpleLogger.EmptyLogger);
        if (!store.IsAvailable)
        {
            return;
        }

        string name = UniqueName("JBTEST");
        try
        {
            Assert.True(store.TrySetSecret(name, "s3cret-pw"));
            Assert.True(store.TryGetSecret(name, out string? found));
            Assert.Equal("s3cret-pw", found);
            Assert.True(store.TryGetSecret(name.ToLowerInvariant(), out _));
        }
        finally
        {
            Assert.True(store.TryRemoveSecret(name));
            Assert.False(store.TryGetSecret(name, out _));
        }
    }

    [Fact]
    public void LinuxSecretToolStore_RoundTrips_WhenAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var store = new LinuxSecretToolStore(ISimpleLogger.EmptyLogger);
        if (!store.IsAvailable)
        {
            return;
        }

        string name = UniqueName("JBTEST");
        try
        {
            Assert.True(store.TrySetSecret(name, "s3cret-pw"));
            Assert.True(store.TryGetSecret(name, out string? found));
            Assert.Equal("s3cret-pw", found);
        }
        finally
        {
            Assert.True(store.TryRemoveSecret(name));
            Assert.False(store.TryGetSecret(name, out _));
        }
    }

    [Fact]
    public void MacKeychainStore_RoundTrips_WhenAvailable()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var store = new MacKeychainStore(ISimpleLogger.EmptyLogger);
        if (!store.IsAvailable)
        {
            return;
        }

        string name = UniqueName("JBTEST");
        try
        {
            Assert.True(store.TrySetSecret(name, "s3cret-pw"));
            Assert.True(store.TryGetSecret(name, out string? found));
            Assert.Equal("s3cret-pw", found);
        }
        finally
        {
            Assert.True(store.TryRemoveSecret(name));
            Assert.False(store.TryGetSecret(name, out _));
        }
    }
}
