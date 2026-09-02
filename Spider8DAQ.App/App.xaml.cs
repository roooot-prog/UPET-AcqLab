using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Do not call base first with StartupUri — license gate owns the first window.
        base.OnStartup(e);

        try
        {
            AppPaths.EnsureWritable(AppPaths.Root);
            AppPaths.EnsureWritable(AppPaths.Logs);
        }
        catch
        {
            // ignore
        }

        bool smokeUi = false;
        foreach (var a in e.Args)
        {
            if (string.Equals(a, "--smoke-ui", StringComparison.OrdinalIgnoreCase))
                smokeUi = true;
        }

        try
        {
            if (!smokeUi && !UpetLicense.IsActivated(out _, out _))
            {
                var dlg = new LicenseWindow();
                var ok = dlg.ShowDialog() == true && dlg.ActivatedOk;
                if (!ok)
                {
                    Shutdown(1);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show(
                "Nu s-a putut deschide fereastra de licență:\n\n" + AppPaths.FriendlyIoMessage(ex),
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            if (!smokeUi && !EnsureApplicationKeyOrShutdown())
                return;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show(
                "Nu s-a putut verifica cheia de aplicație:\n\n" + AppPaths.FriendlyIoMessage(ex),
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            try { UpetReportFileAssociation.EnsureRegistered(); }
            catch { /* non-fatal */ }

            string? openPath = null;
            foreach (var a in e.Args)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                var full = a.Trim('"');
                if (File.Exists(full) &&
                    (Spider8DAQ.Core.Export.UpetReportFile.HasReportExtension(full) ||
                     Spider8DAQ.Core.Export.UpetReportFile.LooksLikeUpetReport(full)))
                {
                    openPath = full;
                    break;
                }
            }

            var main = new MainWindow(openPath);
            MainWindow = main;
            if (smokeUi)
            {
                main.Loaded += (_, _) =>
                    Dispatcher.BeginInvoke(() => Shutdown(0), DispatcherPriority.ApplicationIdle);
            }
            else
            {
                ApplicationKeyHeartbeat.Start();
                Exit += (_, _) => ApplicationKeyHeartbeat.Stop();
            }
            main.Show();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show(
                "Nu s-a putut deschide fereastra principală:\n\n" + AppPaths.FriendlyIoMessage(ex) +
                "\n\nDetalii: " + CrashLogPath(),
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        try
        {
            MessageBox.Show(
                "UPET AcqLab a întâmpinat o eroare:\n\n" + AppPaths.FriendlyIoMessage(e.Exception) +
                "\n\nDetalii în: " + CrashLogPath(),
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { /* ignore */ }
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Empty LicenseServerUrl → skip (author PC). First activation needs the API.
    /// After a cached OK, server downtime does not block LIVE.
    /// </summary>
    private bool EnsureApplicationKeyOrShutdown()
    {
        var cfg = ApplicationKeyConfig.Load();
        if (!cfg.IsConfigured)
            return true;

        var needDialog = !ApplicationKeyStore.HasAcceptedCache();
        if (!needDialog)
        {
            var cached = ApplicationKeyClient.HeartbeatCachedAsync(
                    cfg.NormalizedBaseUrl, TimeSpan.FromSeconds(8))
                .GetAwaiter().GetResult();
            if (cached == ApplicationKeyHeartbeatResult.Ok)
            {
                var rec = ApplicationKeyStore.Load();
                if (rec is not null)
                    ApplicationKeyStore.SaveAccepted(rec.KeyHash, rec.KeyLast4, rec.MachineIdHash, rec.Hostname);
            }
            else if (cached == ApplicationKeyHeartbeatResult.Invalid)
            {
                needDialog = true;
            }
            // Unreachable + cache: allow measurement.
        }

        if (!needDialog)
            return true;

        var dlg = new ApplicationKeyWindow(cfg.NormalizedBaseUrl);
        var ok = dlg.ShowDialog() == true && dlg.ActivatedOk;
        if (ok)
            return true;

        Shutdown(1);
        return false;
    }

    private static string CrashLogPath() => AppPaths.CrashLog;

    private static void LogCrash(Exception ex)
    {
        try
        {
            AppPaths.EnsureWritable(AppPaths.Logs);
            var sb = new StringBuilder();
            sb.AppendLine(DateTime.Now.ToString("O"));
            sb.AppendLine(ex.ToString());
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(CrashLogPath(), sb.ToString());
        }
        catch
        {
            // ignore
        }
    }
}
