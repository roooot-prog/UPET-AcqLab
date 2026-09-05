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
            Assert.Equal(ApplicationKeyConfig.LocalLoopbackUrl, cfg.ResolveHeartbeatUrl());

            File.WriteAllText(Path.Combine(dir, ApplicationKeyConfig.FileName), """{"LicenseServerUrl":"http://127.0.0.1:5088"}""");
            cfg = ApplicationKeyConfig.Load(dir);
            Assert.True(cfg.IsConfigured);
            Assert.Equal("http://127.0.0.1:5088", cfg.NormalizedBaseUrl);

            File.WriteAllText(
                Path.Combine(dir, ApplicationKeyConfig.FileName),
                """
                {
                  "LicenseServerUrl": "http://192.168.100.60:5088",
                  "LicenseServerUrls": [
                    "http://192.168.0.61:5088",
                    "https://von-lions-native-channels.trycloudflare.com",
                    "http://192.168.100.60:5088"
                  ]
                }
                """);
            cfg = ApplicationKeyConfig.Load(dir);
            Assert.Equal(
                new[]
                {
                    "http://192.168.100.60:5088",
                    "http://192.168.0.61:5088",
                    "https://von-lions-native-channels.trycloudflare.com"
                },
                cfg.CandidateUrls);
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

            ApplicationKeyStore.SaveAccepted("abc", "NP23", "machinehash", "LAB-PC", "UPET-APP-ABCD-EFGH-JKLM-NP23");
            json = File.ReadAllText(ApplicationKeyStore.FilePath);
            Assert.DoesNotContain("UPET-APP-ABCD", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ABCD-EFGH-JKLM", json, StringComparison.Ordinal);
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

    [Fact]
    public void TryAcceptApprovedKey_saves_when_status_is_approved()
    {
        var prev = ApplicationKeyStore.OverrideDirectory;
        var dir = Path.Combine(Path.GetTempPath(), "upet-appkey-accept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ApplicationKeyStore.OverrideDirectory = dir;
        try
        {
            Assert.False(ApplicationKeyClient.TryAcceptApprovedKey(new ApplicationKeyLicenseStatus { Status = "pending" }));
            Assert.False(ApplicationKeyClient.TryAcceptApprovedKey(new ApplicationKeyLicenseStatus
            {
                Status = "approved",
                Key = "UPET-APP-ABCD-EFGH-JKLM-NP23",
                KeyLast4 = "NP23"
            }));
            var until = DateTime.UtcNow.AddDays(30);
            Assert.True(ApplicationKeyClient.TryAcceptApprovedKey(new ApplicationKeyLicenseStatus
            {
                Status = "approved",
                Key = "UPET-APP-ABCD-EFGH-JKLM-NP23",
                KeyLast4 = "NP23",
                ValidUntilUtc = until
            }));
            Assert.True(ApplicationKeyStore.HasUnexpiredCache());
            var rec = ApplicationKeyStore.Load();
            Assert.Equal(ApplicationKeyCrypto.HashKey("UPET-APP-ABCD-EFGH-JKLM-NP23"), rec!.KeyHash);
            Assert.NotNull(rec.ValidUntilUtc);
            Assert.True(rec.ValidUntilUtc!.Value > DateTime.UtcNow.AddDays(29));
        }
        finally
        {
            ApplicationKeyStore.OverrideDirectory = prev;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Validity_30_days_and_missing_until_is_expired()
    {
        Assert.Equal(30, ApplicationKeyValidity.DurationDays);
        var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ApplicationKeyValidity.IsExpired(null, now));
        Assert.False(ApplicationKeyValidity.IsExpired(now.AddDays(30), now));
        Assert.True(ApplicationKeyValidity.IsExpired(now.AddDays(-1), now));
        Assert.Equal(now.AddDays(30), ApplicationKeyValidity.FromApprovalUtc(now));
    }

    [Fact]
    public void HasUnexpiredCache_requires_validUntil_in_the_future()
    {
        var prev = ApplicationKeyStore.OverrideDirectory;
        var dir = Path.Combine(Path.GetTempPath(), "upet-appkey-until-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ApplicationKeyStore.OverrideDirectory = dir;
        try
        {
            ApplicationKeyStore.SaveAccepted("abc", "NP23", "machinehash", "LAB-PC");
            Assert.True(ApplicationKeyStore.HasAcceptedCache());
            Assert.False(ApplicationKeyStore.HasUnexpiredCache());

            ApplicationKeyStore.SaveAccepted("abc", "NP23", "machinehash", "LAB-PC", validUntilUtc: DateTime.UtcNow.AddDays(30));
            Assert.True(ApplicationKeyStore.HasUnexpiredCache());

            ApplicationKeyStore.SaveAccepted("abc", "NP23", "machinehash", "LAB-PC", validUntilUtc: DateTime.UtcNow.AddMinutes(-1));
            Assert.False(ApplicationKeyStore.HasUnexpiredCache());
        }
        finally
        {
            ApplicationKeyStore.OverrideDirectory = prev;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void FileVersion_compacts_trailing_zero_revision()
    {
        Assert.Equal("3.3.103", ApplicationKeyIdentity.CompactFileVersion("3.3.103.0"));
        Assert.Equal("3.3.103", ApplicationKeyIdentity.CompactFileVersion("3.3.103"));
        Assert.Equal("3.3.104", ApplicationKeyIdentity.FormatVersionParts(3, 3, 104, 0));
        Assert.Equal("", ApplicationKeyIdentity.CompactFileVersion(""));
        Assert.True(ApplicationKeyIdentity.LooksLikeFullLabVersion("3.3.104"));
        Assert.False(ApplicationKeyIdentity.LooksLikeFullLabVersion("1.103"));
        Assert.NotEqual("1.103", ApplicationKeyIdentity.CompactFileVersion("3.3.103"));
    }

    [Fact]
    public void Licensed_heartbeat_is_2_seconds_and_live_is_1_ms()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ApplicationKeyHeartbeat.LicensedInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(1), ApplicationKeyHeartbeat.LiveInterval);
    }

    [Fact]
    public void Heartbeat_coalesces_pendingMessage_over_adminMessage()
    {
        Assert.Equal("Oprește Rec", ApplicationKeyHeartbeatDetail.CoalescePendingMessage("Oprește Rec", "old"));
        Assert.Equal("USB slăbit", ApplicationKeyHeartbeatDetail.CoalescePendingMessage(null, "  USB slăbit  "));
        Assert.Null(ApplicationKeyHeartbeatDetail.CoalescePendingMessage("  ", ""));
        Assert.Null(ApplicationKeyHeartbeatDetail.CoalescePendingMessage(null, null));
        Assert.Null(ApplicationKeyHeartbeatDetail.CoalescePendingMessage("", "  "));
    }

    [Fact]
    public void ExtendFromUtc_uses_max_of_now_and_expiry_plus_30()
    {
        var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddDays(30), ApplicationKeyValidity.ExtendFromUtc(null, now));
        Assert.Equal(now.AddDays(30), ApplicationKeyValidity.ExtendFromUtc(now.AddDays(-5), now));
        Assert.Equal(now.AddDays(40), ApplicationKeyValidity.ExtendFromUtc(now.AddDays(10), now));
        Assert.Equal(now.AddDays(30), ApplicationKeyValidity.ExtendFromUtc(DateTime.MinValue, now));
    }

    [Fact]
    public void Heartbeat_revoked_is_distinct_from_expired()
    {
        Assert.Equal(ApplicationKeyHeartbeatResult.Revoked, ApplicationKeyHeartbeatDetail.From(ApplicationKeyHeartbeatResult.Revoked).Result);
        Assert.NotEqual(ApplicationKeyHeartbeatResult.Expired, ApplicationKeyHeartbeatResult.Revoked);
        Assert.Equal(ApplicationKeyGateReason.Revoked, ApplicationKeyGateReason.Revoked);
    }

    [Fact]
    public void DaysRemaining_glanceable_romanian_and_urgent()
    {
        var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(12, ApplicationKeyValidity.DaysRemaining(now.AddDays(12).AddHours(3), now));
        Assert.Equal("12", ApplicationKeyValidity.FormatDaysRemainingRo(12, pending: false, expired: false));
        Assert.Equal("Expiră în 3 zile", ApplicationKeyValidity.FormatDaysRemainingRo(3, false, false));
        Assert.Equal("Expiră în 1 zi", ApplicationKeyValidity.FormatDaysRemainingRo(1, false, false));
        Assert.Equal("—", ApplicationKeyValidity.FormatDaysRemainingRo(3, pending: true, expired: false));
        Assert.Equal("0", ApplicationKeyValidity.FormatDaysRemainingRo(0, false, expired: true));
        Assert.True(ApplicationKeyValidity.IsUrgentDays(3, expired: false));
        Assert.False(ApplicationKeyValidity.IsUrgentDays(12, expired: false));
    }

    [Fact]
    public void Remediation_maps_common_strings_without_fake_1893_fail()
    {
        Assert.Contains("Spider8", ApplicationKeyRemediation.Suggest("COMUNICARE PIERDUTĂ — USB")!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start", ApplicationKeyRemediation.Suggest("EST=10003 LED ERROR")!, StringComparison.Ordinal);
        Assert.Contains("Canal gol", ApplicationKeyRemediation.Suggest("Semnal lipsă / punte deschisă")!, StringComparison.Ordinal);
        var shunt = ApplicationKeyRemediation.Suggest("PASS (Half+dummy) residual mic");
        Assert.NotNull(shunt);
        Assert.Contains("ignora", shunt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nu e FAIL", shunt, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ApplicationKeyRemediation.Suggest("totul e bine"));
    }

    [Fact]
    public void CoalescePendingMessage_prefers_pending_then_admin()
    {
        Assert.Equal("a", ApplicationKeyHeartbeatDetail.CoalescePendingMessage("a", "b"));
        Assert.Equal("b", ApplicationKeyHeartbeatDetail.CoalescePendingMessage("  ", "b"));
        Assert.Null(ApplicationKeyHeartbeatDetail.CoalescePendingMessage(null, "  "));
    }
}
