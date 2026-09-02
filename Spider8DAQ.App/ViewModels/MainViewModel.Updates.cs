using System.Windows;
using System.Windows.Input;
using Spider8DAQ.Core.Updates;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    public ICommand CheckUpdatesCommand { get; private set; } = null!;

    private void WireUpdateCommands()
    {
        CheckUpdatesCommand = new RelayCommand(async () =>
            await GitHubUpdateUi.CheckAsync(Application.Current?.MainWindow, interactive: true));
    }

    public async void MaybeCheckUpdatesOnStartup()
    {
        if (!GitHubPrivateUpdater.IsConfigured(GitHubPublicRepo.Owner, GitHubPublicRepo.Repo))
            return;
        try
        {
            await Task.Delay(1200);
            await GitHubUpdateUi.CheckAsync(Application.Current?.MainWindow, interactive: false);
        }
        catch
        {
            /* startup: never block the lab PC */
        }
    }
}
