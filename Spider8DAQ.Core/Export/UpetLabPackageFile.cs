using System.Security.Cryptography;
using System.Text;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Optional password-protected lab package: AES-256 (PBKDF2) wrapping a standard ZIP.
/// Not ZipCrypto — Explorer cannot open .upetlab; only UPET AcqLab (with password).
/// Plain .zip packages need no password and open in Windows Explorer.
/// </summary>
public static class UpetLabPackageFile
{
    public const string Extension = ".upetlab";
    public const string FileFilter =
        "Pachet laborator UPET (*.upetlab)|*.upetlab|ZIP laborator (*.zip)|*.zip";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("UPETLAB1");
    private const ushort FormatVersion = 1;
    private const int SaltLen = 16;
    private const int IvLen = 16;
    private const int KeyLen = 32;
    private const int Pbkdf2Iterations = 120_000;

    public static bool HasPackageExtension(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeEncryptedPackage(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 40) return false;
            Span<byte> m = stackalloc byte[8];
            if (fs.Read(m) != 8) return false;
            return m.SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }

    public static void SaveEncrypted(string path, byte[] zipBytes, string password)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Parola este obligatorie pentru .upetlab.", nameof(password));
        if (zipBytes.Length == 0)
            throw new InvalidOperationException("Pachetul ZIP e gol.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var iv = RandomNumberGenerator.GetBytes(IvLen);
        var key = DeriveKey(password, salt);
        var cipher = AesEncrypt(zipBytes, key, iv);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);
        bw.Write(Magic);
        bw.Write(FormatVersion);
        bw.Write(salt);
        bw.Write(iv);
        bw.Write(cipher.Length);
        bw.Write(cipher);
    }

    public static byte[] LoadEncrypted(string path, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Introduceți parola pachetului.", nameof(password));

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);
        var magic = br.ReadBytes(8);
        if (magic.Length != 8 || !magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Nu este un pachet .upetlab UPET AcqLab.");

        var ver = br.ReadUInt16();
        if (ver != FormatVersion)
            throw new InvalidDataException($"Versiune .upetlab nesuportată ({ver}).");

        var salt = br.ReadBytes(SaltLen);
        var iv = br.ReadBytes(IvLen);
        if (salt.Length != SaltLen || iv.Length != IvLen)
            throw new InvalidDataException("Antet .upetlab corupt.");

        var cipherLen = br.ReadInt32();
        if (cipherLen < 1 || cipherLen > 512_000_000)
            throw new InvalidDataException("Payload .upetlab corupt.");
        var cipher = br.ReadBytes(cipherLen);
        if (cipher.Length != cipherLen)
            throw new InvalidDataException("Payload .upetlab incomplet.");

        var key = DeriveKey(password, salt);
        try
        {
            return AesDecrypt(cipher, key, iv);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Parolă greșită sau fișier deteriorat.", ex);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeyLen);

    private static byte[] AesEncrypt(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    private static byte[] AesDecrypt(byte[] cipher, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }
}
