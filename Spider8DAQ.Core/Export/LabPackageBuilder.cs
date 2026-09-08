using System.IO.Compression;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>Builds the lab handoff folder contents (README + copies); ZIP is created by the caller.</summary>
public static class LabPackageBuilder
{
    public static string BuildReadme(
        ProjectMeta meta,
        string? csvName,
        IEnumerable<string> includedFiles,
        OfflineSession? session = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("UPET AcqLab — Pachet laborator");
        sb.AppendLine(ReportHeaderHelper.UniversityName);
        sb.AppendLine(ReportHeaderHelper.ExpertLine);
        sb.AppendLine(ReportHeaderHelper.AuthorLine);
        sb.AppendLine(new string('=', 48));
        sb.AppendLine($"Creat: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("Experiment");
        sb.AppendLine("----------");
        Line(sb, "Proiect", meta.ProjectName);
        Line(sb, "Operator", meta.Operator);
        Line(sb, "Probă / Sample", meta.SampleId);
        foreach (var (label, value) in Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildReportMetaRows(meta))
            Line(sb, label, value);
        // Specimen: SampleLengthMm / SampleMassG / SampleDimensionsSummary (0 = omit)
        foreach (var (label, value) in SampleDimensions.BuildReportMetaRows(meta))
            Line(sb, label, value);
        foreach (var (label, value) in StrainAnalysisIndicators.BuildReportMetaRows(meta))
            Line(sb, label, value);
        Line(sb, "Tip", meta.ExperimentType);
        if (CylinderContourExport.ShouldAttempt(meta, session) && meta.CylinderContour is not null)
            Line(sb, "Contur cilindru", meta.CylinderContour.ToStatusSummary());
        Line(sb, "Preset", meta.ExperimentName);
        Line(sb, "Locație", meta.Location);
        Line(sb, "Backend", meta.Backend);
        Line(sb, "Rată [Hz]", meta.SampleRateHz > 0 ? meta.SampleRateHz.ToString() : "");
        Line(sb, "Start", meta.ExperimentStartLocal);
        Line(sb, "Stop", meta.ExperimentEndLocal);
        Line(sb, "Durată est. [min]", meta.EstimatedDurationMinutes > 0 ? meta.EstimatedDurationMinutes.ToString() : "");
        Line(sb, "Senzori", meta.PlannedSensors);
        Line(sb, "Rezumat senzori", meta.SensorSummary);
        Line(sb, "Comentariu", meta.Comment);
        Line(sb, "Note montaj înainte", meta.MontageBeforeNotes);
        Line(sb, "Note montaj după", meta.MontageAfterNotes);
        Line(sb, "Cameră film experiment", meta.ExperimentCameraName);
        Line(sb, "Clip experiment", Path.GetFileName(meta.ExperimentVideoPath ?? ""));
        Line(sb, "Calibrare", meta.CalibrationNotes);
        Line(sb, "Amprentă măsurare", meta.MeasurementFingerprint);
        Line(sb, "Amprentă algo", meta.FingerprintAlgo);

        var industrial = IndustrialReportSections.Build(
            session ?? new OfflineSession(),
            meta,
            new IndustrialReportSections.Options
            {
                CsvPath = MeasurementFingerprint.ResolveExistingCsvPath(session?.SourcePath)
            });
        IndustrialReportSections.AppendPlainTextSections(sb, industrial, meta);

        sb.AppendLine();
        sb.AppendLine("Fișiere în pachet");
        sb.AppendLine("-----------------");
        if (!string.IsNullOrWhiteSpace(csvName))
            sb.AppendLine("  CSV: " + csvName);
        foreach (var f in includedFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            sb.AppendLine("  - " + f);
        sb.AppendLine();
        sb.AppendLine("Note");
        sb.AppendLine("----");
        sb.AppendLine("  • Amprenta (UPET-XXXX-…) = sigiliu de integritate: semnal + context experiment.");
        sb.AppendLine("  • Verificare: deschideți CSV/.upet în Analiză — badge Validă / Alterată.");
        sb.AppendLine("  • .upet = raport proprietar (date + grafice + poze) — deschideți în UPET AcqLab.");
        sb.AppendLine("  • .zip fără parolă = deschideți în Explorer.");
        sb.AppendLine("  • .upetlab = același ZIP, criptat AES cu parolă — doar UPET AcqLab.");
        sb.AppendLine("  • *_video.mp4 = film epruvetă pe durata Record (cameră USB), separat de pozele montaj.");
        return sb.ToString();
    }

    /// <summary>MP4-uri existente din meta experiment (ultimele Rec-uri).</summary>
    public static IReadOnlyList<string> ExistingExperimentVideos(ProjectMeta? meta)
    {
        var list = new List<string>();
        if (meta is null) return list;
        void add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (!list.Contains(path, StringComparer.OrdinalIgnoreCase))
                list.Add(path);
        }

        add(meta.ExperimentVideoPath);
        if (meta.ExperimentVideoFiles is not null)
        {
            foreach (var p in meta.ExperimentVideoFiles)
                add(p);
        }

        return list;
    }

    public static void CopyExperimentVideos(ProjectMeta? meta, string destDirectory, ICollection<string> included)
    {
        foreach (var path in ExistingExperimentVideos(meta))
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) continue;
            TryCopy(path, destDirectory, name);
            if (!included.Contains(name, StringComparer.OrdinalIgnoreCase))
                included.Add(name);
        }
    }

    public static void WriteReadme(string directory, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README_laborator.txt"), text, Encoding.UTF8);
    }

    public static void TryCopy(string? sourcePath, string destDirectory, string destFileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
        Directory.CreateDirectory(destDirectory);
        var dest = Path.Combine(destDirectory, destFileName);
        File.Copy(sourcePath, dest, overwrite: true);
    }

    public static byte[] ZipDirectoryToBytes(string sourceDirectory)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "upet_labpack_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return File.ReadAllBytes(zipPath);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { /* ignore */ }
        }
    }

    public static void ExtractZipBytes(byte[] zipBytes, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var zipPath = Path.Combine(Path.GetTempPath(), "upet_labpack_in_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            File.WriteAllBytes(zipPath, zipBytes);
            ZipFile.ExtractToDirectory(zipPath, destinationDirectory, overwriteFiles: true);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { /* ignore */ }
        }
    }

    private static void Line(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"  {label}: {value.Trim()}");
    }
}
