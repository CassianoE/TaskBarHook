using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TaskBarHook.Queue;

public sealed class ProtectedSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskBarHook.Spotify");

    private readonly string _directory;

    public ProtectedSecretStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskBarHook",
            "secrets");
        Directory.CreateDirectory(_directory);
    }

    public string? Get(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Set(string key, string value)
    {
        Directory.CreateDirectory(_directory);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            Entropy,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), protectedBytes);
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '_');
        }

        return Path.Combine(_directory, key + ".bin");
    }
}
