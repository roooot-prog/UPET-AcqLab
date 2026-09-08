using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>Finds the Rec MP4 next to a CSV / .upet / extracted lab package.</summary>
public static class ExperimentVideoLocator
{
    public static string? Find(
        string? sourcePath,
        ProjectMeta? meta,
        IEnumerable<string>? extraFiles = null,
        params string[] extraDirectories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? best = null;
        var stem = StemOf(sourcePath);

        void consider(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            if (!IsVideo(path)) return;
            if (!seen.Add(Path.GetFullPath(path))) return;
            if (best is null)
            {
                best = path;
                return;
            }

            if (!string.IsNullOrWhiteSpace(stem)
                && Path.GetFileName(path).Contains(stem, StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(best).Contains(stem, StringComparison.OrdinalIgnoreCase))
                best = path;
        }

        foreach (var p in LabPackageBuilder.ExistingExperimentVideos(meta))
            consider(p);
        if (extraFiles is not null)
        {
            foreach (var p in extraFiles)
                consider(p);
        }

        foreach (var dir in DirectoriesToScan(sourcePath, extraDirectories))
        {
            try
            {
                foreach (var p in Directory.EnumerateFiles(dir, "*_video.mp4", SearchOption.TopDirectoryOnly))
                    consider(p);
                foreach (var p in Directory.EnumerateFiles(dir, "*.mp4", SearchOption.TopDirectoryOnly))
                    consider(p);
            }
            catch
            {
                /* ignore */
            }
        }

        return best;
    }

    private static IEnumerable<string> DirectoriesToScan(string? sourcePath, string[] extraDirectories)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void add(string? d)
        {
            if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d)) return;
            set.Add(Path.GetFullPath(d));
        }

        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            if (File.Exists(sourcePath))
                add(Path.GetDirectoryName(sourcePath));
            else if (Directory.Exists(sourcePath))
                add(sourcePath);
        }

        if (extraDirectories is not null)
        {
            foreach (var d in extraDirectories)
                add(d);
        }

        return set;
    }

    private static string StemOf(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return "";
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        if (name.EndsWith("_raport", StringComparison.OrdinalIgnoreCase))
            name = name[..^"_raport".Length];
        return name;
    }

    private static bool IsVideo(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".wmv" or ".mkv" or ".avi" or ".mov";
    }
}
