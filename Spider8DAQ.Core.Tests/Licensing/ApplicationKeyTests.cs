using System.IO;
using System.Text.Json;
using Xunit;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.Core.Tests.Licensing;

public class ApplicationKeyTests
{
    [Fact]
    public void HashKey_is_stable_and_normalized()
    {
        var a = ApplicationKeyCrypto.HashKey("upet-app-abcd-efgh-jklm-npqr");
        var b = ApplicationKeyCrypto.HashKey("  UPET-APP-ABCD-EFGH-JKLM-NPQR  ");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void LooksLikeApplicationKey_accepts_generated_shape()
    {
        Assert.True(ApplicationKeyCrypto.LooksLikeApplicationKey("UPET-APP-ABCD-EFGH-JKLM-NP23"));
        Assert.False(ApplicationKeyCrypto.LooksLikeApplicationKey("UPET-ACQLAB-XH3T-2AAA-QKN2-NGST"));
        Assert.False(ApplicationKeyCrypto.LooksLikeApplicationKey(""));
    }

    [Fact]
    public void MaskKey_keeps_only_last_four()
    {
        var masked = ApplicationKeyCrypto.MaskKey("UPET-APP-ABCD-EFGH-JKLM-NP23");
        Assert.Equal("UPET-APP-••••-••••-••••-NP23", masked);
        Assert.DoesNotContain("ABCD", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_LicenseServerUrl_is_not_configured()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upet-appkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ApplicationKeyConfig.FileName), """{"LicenseServerUrl":""}""");
            var cfg = ApplicationKeyConfig.Load(dir);
            Assert.False(cfg.IsConfigured);

            File.WriteAllText(Path.Combine(dir, ApplicationKeyConfig.FileName), """{"LicenseServerUrl":"http://127.0.0.1:5088"}""");
            cfg = ApplicationKeyConfig.Load(dir);
            Assert.True(cfg.IsConfigured);
            Assert.Equal("http://127.0.0.1:5088", cfg.NormalizedBaseUrl);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Store_persists_hash_not_raw_key()
    {
        var prev = ApplicationKeyStore.OverrideDirectory;
        var dir = Path.Combine(Path.GetTempPath(), "upet-appkey-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ApplicationKeyStore.OverrideDirectory = dir;
        try
        {
            ApplicationKeyStore.SaveAccepted("abc", "NP23", "machinehash", "LAB-PC");
            Assert.True(ApplicationKeyStore.HasAcceptedCache());
            var rec = ApplicationKeyStore.Load();
            Assert.NotNull(rec);
            Assert.Equal("abc", rec!.KeyHash);
            var json = File.ReadAllText(ApplicationKeyStore.FilePath);
            Assert.DoesNotContain("UPET-APP", json, StringComparison.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.TryGetProperty("key", out _));
        }
        finally
        {
            ApplicationKeyStore.OverrideDirectory = prev;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HashedMachineId_is_not_the_raw_hostname()
    {
        var id = ApplicationKeyCrypto.HashedMachineId();
        Assert.Equal(64, id.Length);
        Assert.NotEqual(Environment.MachineName, id);
        Assert.DoesNotContain(Environment.MachineName, id, StringComparison.OrdinalIgnoreCase);
    }
}
