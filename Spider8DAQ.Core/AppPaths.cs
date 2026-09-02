namespace Spider8DAQ.Core;

/// <summary>
/// Writable user data under %LocalAppData%\UPETAcqLab\.
/// Install / vendor assets stay next to the EXE (may be read-only under Program Files).
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "UPETAcqLab";

    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);

    public static string InstallRoot =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string Camera => Path.Combine(Root, "camera");
    public static string Recordings => Path.Combine(Root, "recordings");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Data => Path.Combine(Root, "data");
    public static string Snapshots => Path.Combine(Root, "snapshots");
    public static string Examples => Path.Combine(Root, "examples");
    public static string Templates => Path.Combine(Root, "templates");
    public static string Vendor => Path.Combine(Root, "vendor");
    public static string Projects => Path.Combine(Root, "projects");

    public static string SensorsCatalog => Path.Combine(Vendor, "sensors.json");
    public static string FormulasLibrary => Path.Combine(Vendor, "formulas.json");
    public static string IntegrationsSettings => Path.Combine(Data, "integrations.json");
    public static string MeasurementsDb => Path.Combine(Data, "measurements.db");
    public static string AppLog => Path.Combine(Logs, "spider8daq.log");
    public static string CrashLog => Path.Combine(Logs, "startup-crash.log");
    /// <summary>Local learning store for lab advisor feedback (helpful / not).</summary>
    public static string AdvisorMemory => Path.Combine(Data, "advisor-memory.json");
    /// <summary>UI prefs for lab advisor (panel visibility).</summary>
    public static string AdvisorPrefs => Path.Combine(Data, "advisor-prefs.json");
    /// <summary>Specimen library: last selected id + operator custom cards.</summary>
    public static string SpecimenLibrary => Path.Combine(Data, "specimen-library.json");
    /// <summary>Lab prefs (shunt allowed difference, etc.).</summary>
    public static string LabPrefs => Path.Combine(Data, "lab-prefs.json");
    /// <summary>Downloaded GitHub update zips and apply helper.</summary>
    public static string Updates => Path.Combine(Root, "updates");

    public static string BundledVendorFile(string fileName) =>
        Path.Combine(InstallRoot, "vendor", fileName);

    public static string BundledExamples => Path.Combine(InstallRoot, "examples");

    /// <summary>Creates the directory; wraps access-denied with a clear Romanian message.</summary>
    public static string EnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(FormatAccessDenied(directory), ex);
        }
        catch (IOException ex) when (IsAccessDenied(ex))
        {
            throw new UnauthorizedAccessException(FormatAccessDenied(directory), ex);
        }
    }

    public static void EnsureUserDataLayout()
    {
        EnsureWritable(Root);
        EnsureWritable(Camera);
        EnsureWritable(Recordings);
        EnsureWritable(Logs);
        EnsureWritable(Data);
        EnsureWritable(Snapshots);
        EnsureWritable(Examples);
        EnsureWritable(Templates);
        EnsureWritable(Vendor);
        EnsureWritable(Projects);
        EnsureWritable(Updates);
    }

    public static string FormatAccessDenied(string? path)
    {
        var p = string.IsNullOrWhiteSpace(path) ? "(necunoscut)" : path;
        return
            "Acces refuzat la scriere:\n" + p +
            "\n\nInstalarea din Program Files este numai pentru citire. Datele se salvează în:\n" +
            Root +
            "\n\nDacă vedeți o cale sub Program Files, aplicația trebuie actualizată.";
    }

    public static string FriendlyIoMessage(Exception ex, string? path = null)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is UnauthorizedAccessException)
                return FormatAccessDenied(path ?? TryExtractPath(e.Message));
        }

        return ex.Message;
    }

    private static bool IsAccessDenied(Exception ex) =>
        ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("refuzat", StringComparison.OrdinalIgnoreCase)
        || ex.HResult == unchecked((int)0x80070005); // E_ACCESSDENIED

    private static string? TryExtractPath(string message)
    {
        // .NET: Access to the path 'X' is denied.
        var i = message.IndexOf('\'');
        var j = i >= 0 ? message.LastIndexOf('\'') : -1;
        if (i >= 0 && j > i)
            return message.Substring(i + 1, j - i - 1);
        return null;
    }
}
