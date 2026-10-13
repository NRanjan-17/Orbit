using System.Security.Cryptography;

namespace Orbit.Platform;

/// <summary>
/// Stores the headphones' encryption key encrypted with Windows DPAPI (CurrentUser scope), so only this Windows
/// account on this PC can read it. Never written to settings.json, the log, or the repository.
/// </summary>
public sealed class KeyStore(string path)
{
    // Extra entropy so other apps using DPAPI for the same user can't trivially decrypt Orbit's blob by guessing.
    private static readonly byte[] Entropy = "Orbit.ProximityKey.v1"u8.ToArray();

    public string Path { get; } = path;

    public bool HasKey => File.Exists(Path);

    public byte[]? Load()
    {
        try
        {
            return File.Exists(Path) ? ProtectedData.Unprotect(File.ReadAllBytes(Path), Entropy, DataProtectionScope.CurrentUser) : null;
        }
        catch (CryptographicException)
        {
            return null; // copied from another account/PC, or corrupted
        }
    }

    public void Save(byte[] key)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllBytes(Path, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
    }

    public void Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
