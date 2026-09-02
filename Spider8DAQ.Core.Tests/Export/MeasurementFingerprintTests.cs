using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Analysis;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class MeasurementFingerprintTests
{
    [Fact]
    public async Task SealCsv_ThenVerify_IsValid_AndDetectsTamper()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_fp_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
            await using (var w = new CsvRecordingWriter(path))
            {
                w.SetRateHzSet(50);
                w.WriteMetaComments(new[]
                {
                    "UPET AcqLab test",
                    "Operator=Lab",
                    "Sample=P01",
                    "Backend=Simulator",
                    "RateHzSet=50",
                    "Comment=fingerprint-test"
                });
                w.WriteHeader(new[] { "CH0" });
                for (var i = 0; i < 4; i++)
                {
                    w.WriteSample(
                        new SampleFrame
                        {
                            Timestamp = t0.AddMilliseconds(i * 20),
                            Sequence = i,
                            Values = Array.Empty<double>()
                        },
                        new[] { i * 1.5 });
                }
                w.WriteEffectiveRateFooter();
            }

            var meta = new ProjectMeta
            {
                Operator = "Lab",
                SampleId = "P01",
                Backend = "Simulator",
                SampleRateHz = 50,
                Comment = "fingerprint-test"
            };
            var seal = MeasurementFingerprint.SealCsvFile(path, meta);
            Assert.StartsWith("UPET-", seal.Code);
            Assert.Equal(4, seal.Code.Split('-').Length);

            var ok = MeasurementFingerprint.VerifyCsvFile(path);
            Assert.Equal(MeasurementFingerprint.VerifyStatus.Valid, ok.Status);
            Assert.Equal(seal.Code, ok.StoredCode);

            // Tamper a data value
            var text = File.ReadAllText(path);
            var tampered = text.Replace("1.5", "9.9", StringComparison.Ordinal);
            Assert.NotEqual(text, tampered);
            File.WriteAllText(path, tampered);

            var bad = MeasurementFingerprint.VerifyCsvFile(path);
            Assert.Equal(MeasurementFingerprint.VerifyStatus.Altered, bad.Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ComputeForSession_RoundTrip_MatchesStoredCode()
    {
        var session = new OfflineSession
        {
            ChannelNames = new List<string> { "CH0" },
            Timestamps = new List<DateTime>
            {
                new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
                new(2026, 8, 15, 10, 0, 0, 20, DateTimeKind.Utc)
            },
            Sequences = new List<long> { 0, 1 },
            Columns = new List<double[]> { new[] { 1.0, 2.0 } }
        };
        var meta = new ProjectMeta
        {
            Operator = "UPET",
            SampleId = "S1",
            Backend = "Simulator",
            SampleRateHz = 50,
            SensorSummary = "CH0: demo"
        };
        var seal = MeasurementFingerprint.ComputeForSession(session, meta);
        MeasurementFingerprint.ApplySealToMeta(meta, seal);
        var vr = MeasurementFingerprint.VerifySession(session, meta);
        Assert.Equal(MeasurementFingerprint.VerifyStatus.Valid, vr.Status);
        Assert.Equal(seal.Code, vr.StoredCode);
    }

    [Fact]
    public void ResolveReportStatusLabel_NeverLeavesDash()
    {
        var session = new OfflineSession
        {
            ChannelNames = { "CH0" },
            Timestamps =
            {
                new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 15, 10, 0, 1, DateTimeKind.Utc)
            },
            Sequences = { 0, 1 },
            Columns = { new[] { 1.0, 2.0 } }
        };
        var metaEmpty = new ProjectMeta();
        Assert.Equal("Lipsă", MeasurementFingerprint.ResolveReportStatusLabel(session, metaEmpty));

        var metaCode = new ProjectMeta { MeasurementFingerprint = "UPET-DEAD-BEEF-0001" };
        var label = MeasurementFingerprint.ResolveReportStatusLabel(session, metaCode);
        Assert.Equal("Alterată", label);
        Assert.NotEqual("—", label);

        Assert.Equal("Validă",
            MeasurementFingerprint.ResolveReportStatusLabel(session, metaCode, explicitLabel: "Validă · x"));
        Assert.Equal(MeasurementFingerprint.LabelNeverificata,
            MeasurementFingerprint.ResolveReportStatusLabel(null, metaCode));
        Assert.Equal("#2E7D32", MeasurementFingerprint.StatusColorHex("Validă"));
        Assert.Equal("[ALTERAT]", MeasurementFingerprint.StatusAsciiMarker("Alterată"));
    }

    /// <summary>
    /// Regression: Excel/HTML/industrial export used VerifySession(rich CurrentProjectMeta)
    /// against a CSV seal (slim meta + file bytes) → false "Alterată" on fresh in-app export.
    /// </summary>
    [Fact]
    public async Task ResolveReportStatusLabel_CsvSealed_RichMeta_IsValidNotAltered()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_fp_rich_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
            await using (var w = new CsvRecordingWriter(path))
            {
                w.SetRateHzSet(50);
                w.WriteMetaComments(new[]
                {
                    "UPET AcqLab test",
                    "Operator=Lab",
                    "Sample=P01",
                    "Backend=Simulator",
                    "RateHzSet=50",
                    "Comment=fingerprint-test"
                });
                w.WriteHeader(new[] { "CH0" });
                for (var i = 0; i < 4; i++)
                {
                    w.WriteSample(
                        new SampleFrame
                        {
                            Timestamp = t0.AddMilliseconds(i * 20),
                            Sequence = i,
                            Values = Array.Empty<double>()
                        },
                        new[] { i * 1.5 });
                }
                w.WriteEffectiveRateFooter();
            }

            var slim = new ProjectMeta
            {
                Operator = "Lab",
                SampleId = "P01",
                Backend = "Simulator",
                SampleRateHz = 50,
                Comment = "fingerprint-test"
            };
            var seal = MeasurementFingerprint.SealCsvFile(path, slim);

            // Mimic CurrentProjectMeta() after Rec: rich fields + UI code, session from CSV.
            var rich = new ProjectMeta
            {
                Operator = "Lab",
                SampleId = "P01",
                Backend = "Simulator",
                SampleRateHz = 50,
                Comment = "fingerprint-test",
                Location = "Universitatea din Petroșani — laborator",
                ProjectName = "Demo",
                ExperimentName = "Încercare",
                SensorSummary = "CH0: demo",
                ActiveChannelCount = 1,
                MeasurementFingerprint = seal.Code
            };
            var session = OfflineSession.FromCsv(path);

            // Pre-fix behavior: VerifySession(rich) alone → Altered (payload + meta mismatch).
            Assert.Equal(
                MeasurementFingerprint.VerifyStatus.Altered,
                MeasurementFingerprint.VerifySession(session, rich).Status);

            Assert.Equal(
                "Validă",
                MeasurementFingerprint.ResolveReportStatusLabel(session, rich));

            // UI badge after Rec must not short-circuit to a non-status phrase.
            Assert.Equal(
                "Validă",
                MeasurementFingerprint.ResolveReportStatusLabel(
                    session, rich, explicitLabel: "Amprentă generată"));

            // Sticky UI "Alterată" (from earlier VerifySession false-positive) must not poison reports.
            Assert.Equal(
                "Validă",
                MeasurementFingerprint.ResolveReportStatusLabel(
                    session, rich, explicitLabel: "Alterată", csvPath: path));

            // ExportRegion-style SourcePath fragment must still resolve the sealed CSV.
            var regionish = OfflineSession.FromCsv(path);
            regionish.SourcePath = path + "#[0,4)";
            Assert.Equal(
                "Validă",
                MeasurementFingerprint.ResolveReportStatusLabel(regionish, rich));

            // Empty SourcePath without csvPath → false Alterată; with csvPath → Validă.
            var noPath = OfflineSession.FromCsv(path);
            noPath.SourcePath = "";
            Assert.Equal(
                "Alterată",
                MeasurementFingerprint.ResolveReportStatusLabel(noPath, rich));
            Assert.Equal(
                "Validă",
                MeasurementFingerprint.ResolveReportStatusLabel(noPath, rich, csvPath: path));

            var rows = IndustrialReportSections.BuildCommonMetaRows(
                noPath, rich,
                new IndustrialReportSections.Options { CsvPath = path });
            var stare = rows.First(r => r.Label.Contains("Stare amprent", StringComparison.OrdinalIgnoreCase)).Value;
            Assert.Equal("Validă", stare);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
