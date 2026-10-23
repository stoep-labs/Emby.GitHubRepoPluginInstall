using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Emby.GitHubRepoPluginInstall.Security;
using MediaBrowser.Model.Logging;
using NSubstitute;
using Xunit;

namespace Emby.GitHubRepoPluginInstall.Tests;

public class SecureStorageTests : IDisposable
{
    private const string Token = "ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef0123";

    private readonly string  _dir     = Path.Combine(Path.GetTempPath(), "SecureStorageTests-" + Guid.NewGuid().ToString("N"));
    private readonly ILogger _logger  = Substitute.For<ILogger>();
    private          string  KeyFile => Path.Combine(_dir, "plugin.key");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // The pre-fix format: XOR with SHA256(machine name + salt)
    private static string LegacyProtect(string plainText, string machineName)
    {
        using var sha256 = SHA256.Create();
        var key   = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineName + "EmbyGitHubPlugin2024"));
        var bytes = Encoding.UTF8.GetBytes(plainText);
        for (int i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
        return Convert.ToBase64String(bytes);
    }

    [Theory]
    [InlineData(Token)]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public void RoundTrip_ReturnsOriginalToken(string token)
    {
        var storage = new SecureStorage(_logger, KeyFile, "host-1");

        var result = storage.Unprotect(storage.Protect(token), out var usedLegacyKey);

        Assert.Equal(token, result);
        Assert.False(usedLegacyKey);
    }

    [Fact]
    public void NewKey_SurvivesHostnameChange()
    {
        var protectedText = new SecureStorage(_logger, KeyFile, "container-aaaa").Protect(Token);

        var result = new SecureStorage(_logger, KeyFile, "container-bbbb").Unprotect(protectedText);

        Assert.Equal(Token, result);
    }

    [Fact]
    public void KeyFile_IsCreatedOnceAndNotOverwritten()
    {
        new SecureStorage(_logger, KeyFile, "host-1");
        var first = File.ReadAllText(KeyFile);

        new SecureStorage(_logger, KeyFile, "host-2");

        Assert.Equal(first, File.ReadAllText(KeyFile));
    }

    [Fact]
    public void LegacyToken_SameMachineName_IsMigrated()
    {
        var storage = new SecureStorage(_logger, KeyFile, "host-1");

        var result = storage.Unprotect(LegacyProtect(Token, "host-1"), out var usedLegacyKey);

        Assert.Equal(Token, result);
        Assert.True(usedLegacyKey);
        Assert.Equal(Token, storage.Unprotect(storage.Protect(result), out usedLegacyKey));
        Assert.False(usedLegacyKey);
    }

    [Fact]
    public void LegacyToken_WithSurroundingWhitespace_IsMigratedTrimmed()
    {
        var storage = new SecureStorage(_logger, KeyFile, "host-1");

        var result = storage.Unprotect(LegacyProtect(" " + Token + " \n", "host-1"), out var usedLegacyKey);

        Assert.Equal(Token, result);
        Assert.True(usedLegacyKey);
    }

    [Theory]
    [InlineData(Token)]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public void LegacyToken_MachineNameChanged_ReturnsEmptyNotGarbage(string token)
    {
        var storage = new SecureStorage(_logger, KeyFile, "container-bbbb");

        var result = storage.Unprotect(LegacyProtect(token, "container-aaaa"), out var usedLegacyKey);

        Assert.Equal(string.Empty, result);
        Assert.False(usedLegacyKey);
        _logger.Received(1).Warn(Arg.Any<string>(), Arg.Any<object[]>());
    }

    [Fact]
    public void GarbageSavedUnderLegacyKey_ReturnsEmpty()
    {
        // A page saved while it showed garbage re-encrypted that garbage under the current hostname
        var garbage = Encoding.UTF8.GetString(Convert.FromBase64String(LegacyProtect(Token, "container-aaaa")));
        Assert.False(SecureStorage.IsPlausibleToken(garbage));

        var storage = new SecureStorage(_logger, KeyFile, "container-bbbb");

        Assert.Equal(string.Empty, storage.Unprotect(LegacyProtect(garbage, "container-bbbb")));
    }

    [Fact]
    public void PlainTextToken_FromOldVersion_IsAccepted()
    {
        var storage = new SecureStorage(_logger, KeyFile, "host-1");

        Assert.Equal(Token, storage.Unprotect(Token));
    }

    [Fact]
    public void NotBase64AndNotAToken_ReturnsEmpty()
    {
        var storage = new SecureStorage(_logger, KeyFile, "host-1");

        Assert.Equal(string.Empty, storage.Unprotect("not a token!"));
    }

    [Fact]
    public void CorruptKeyFile_IsReplaced()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(KeyFile, "corrupt");

        var storage = new SecureStorage(_logger, KeyFile, "host-1");

        Assert.Equal(Token, storage.Unprotect(storage.Protect(Token)));
        Assert.Equal(32, Convert.FromBase64String(File.ReadAllText(KeyFile)).Length);
    }
}
