using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace Emby.GitHubRepoPluginInstall.Security;

public class SecureStorage : ISecureStorage
{
    private const int KeyLength = 32;

    private static readonly string[] KnownTokenPrefixes = { "ghp_", "github_pat_", "gho_", "ghu_", "ghs_", "ghr_" };
    private static readonly object   KeyFileLock        = new object();

    private readonly ILogger _logger;
    private readonly byte[]  _key;
    private readonly byte[]  _legacyKey;
    private          string  _lastWarnedProtectedText;

    // keyFilePath must live on persistent storage (the Emby config dir), so the key survives a recreated Docker container
    public SecureStorage(ILogger logger, string keyFilePath) : this(logger, keyFilePath, Environment.MachineName)
    {
    }

    public SecureStorage(ILogger logger, string keyFilePath, string legacyMachineName)
    {
        _logger    = logger;
        _key       = LoadOrCreateKey(keyFilePath);
        _legacyKey = CreateLegacyKey(legacyMachineName);
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        // Simple XOR-based obfuscation (not secure encryption, just basic hiding)
        return Convert.ToBase64String(Xor(Encoding.UTF8.GetBytes(plainText), _key));
    }

    public string Unprotect(string protectedText)
    {
        return Unprotect(protectedText, out _);
    }

    public string Unprotect(string protectedText, out bool usedLegacyKey)
    {
        usedLegacyKey = false;

        if (string.IsNullOrEmpty(protectedText))
            return protectedText;

        byte[] obfuscatedBytes;
        try
        {
            obfuscatedBytes = Convert.FromBase64String(protectedText);
        }
        catch (FormatException)
        {
            // Not base64: accept it only if it is a plain-text token from an old version
            var plainText = protectedText.Trim();
            if (HasKnownTokenPrefix(plainText) && IsPlausibleToken(plainText))
                return plainText;

            WarnUnreadable(protectedText);
            return string.Empty;
        }

        var result = Decode(obfuscatedBytes, _key);
        if (IsPlausibleToken(result))
            return result;

        // Tokens saved before the key file existed were keyed on the machine name, which still works while the hostname is unchanged
        result = Decode(obfuscatedBytes, _legacyKey);
        if (IsPlausibleToken(result))
        {
            usedLegacyKey = true;
            return result;
        }

        WarnUnreadable(protectedText);
        return string.Empty;
    }

    public Task<string> ProtectAsync(string plainText)
    {
        return Task.FromResult(Protect(plainText));
    }

    public Task<string> UnprotectAsync(string protectedText)
    {
        return Task.FromResult(Unprotect(protectedText));
    }

    public static bool IsPlausibleToken(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 20 || value.Length > 255)
            return false;

        // Printable ASCII without spaces; rejects control chars and the U+FFFD that a wrong key produces
        return value.All(c => c > 0x20 && c < 0x7F);
    }

    private static bool HasKnownTokenPrefix(string value)
    {
        return KnownTokenPrefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal));
    }

    private void WarnUnreadable(string protectedText)
    {
        // GetOptions decrypts on every call, so warn once per stored value
        if (protectedText == _lastWarnedProtectedText)
            return;

        _lastWarnedProtectedText = protectedText;
        _logger.Warn("The saved GitHub token could not be decrypted (it was saved on a different machine or container). Re-enter the token on the plugin page.");
    }

    private static string Decode(byte[] obfuscatedBytes, byte[] key)
    {
        // Tokens pasted with surrounding whitespace were saved as-is by older versions
        return Encoding.UTF8.GetString(Xor(obfuscatedBytes, key)).Trim();
    }

    private static byte[] Xor(byte[] data, byte[] key)
    {
        var output = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            output[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return output;
    }

    private static byte[] CreateLegacyKey(string machineName)
    {
        using (var sha256 = SHA256.Create())
        {
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineName + "EmbyGitHubPlugin2024"));
        }
    }

    private byte[] LoadOrCreateKey(string keyFilePath)
    {
        lock (KeyFileLock)
        {
            var corrupt = false;
            try
            {
                if (File.Exists(keyFilePath))
                {
                    var existing = Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
                    if (existing.Length == KeyLength)
                        return existing;

                    corrupt = true;
                }
            }
            catch (FormatException)
            {
                corrupt = true;
            }
            catch (Exception ex)
            {
                // Unreadable is not corrupt: CreateNew below fails rather than overwrite it, and the key stays in memory
                _logger.Error("Failed to read token key file {0}: {1}", keyFilePath, ex.Message);
            }

            var key = RandomNumberGenerator.GetBytes(KeyLength);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(keyFilePath));

                // CreateNew never overwrites a valid key; only a corrupt one is replaced
                using (var stream = new FileStream(keyFilePath, corrupt ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
                {
                    var bytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(key));
                    stream.Write(bytes, 0, bytes.Length);
                }

                if (corrupt)
                    _logger.Warn("Token key file {0} was corrupt and has been replaced. Re-enter the GitHub token on the plugin page.", keyFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save token key file {0}: {1}. The GitHub token will not survive a restart.", keyFilePath, ex.Message);
            }

            return key;
        }
    }
}
