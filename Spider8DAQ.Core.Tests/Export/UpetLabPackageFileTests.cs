using System.IO.Compression;
using System.Text;
using Spider8DAQ.Core.Export;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class UpetLabPackageFileTests
{
    [Fact]
    public void RoundTrip_EncryptedZip_WithPassword()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upet_labpack_t_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "README_laborator.txt"), "hello lab", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(dir, "data.bin"), [1, 2, 3, 4, 5]);

        var zipBytes = LabPackageBuilder.ZipDirectoryToBytes(dir);
        var path = Path.Combine(dir, "pack.upetlab");
        try
        {
            UpetLabPackageFile.SaveEncrypted(path, zipBytes, "secret-lab");
            Assert.True(UpetLabPackageFile.LooksLikeEncryptedPackage(path));

            var loaded = UpetLabPackageFile.LoadEncrypted(path, "secret-lab");
            Assert.Equal(zipBytes.Length, loaded.Length);

            var outDir = Path.Combine(dir, "out");
            LabPackageBuilder.ExtractZipBytes(loaded, outDir);
            Assert.True(File.Exists(Path.Combine(outDir, "README_laborator.txt")));
            Assert.Equal("hello lab", File.ReadAllText(Path.Combine(outDir, "README_laborator.txt")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LoadEncrypted_WrongPassword_Throws()
    {
        var zipBytes = Encoding.UTF8.GetBytes("PK\x03\x04fake"); // not a real zip; crypto layer only
        // Use a minimal valid zip from ZipArchive
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("a.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("x");
        }
        zipBytes = ms.ToArray();

        var path = Path.Combine(Path.GetTempPath(), "badpass_" + Guid.NewGuid().ToString("N") + ".upetlab");
        try
        {
            UpetLabPackageFile.SaveEncrypted(path, zipBytes, "good");
            Assert.ThrowsAny<Exception>(() => UpetLabPackageFile.LoadEncrypted(path, "wrong"));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
