using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Mosaic.Models;
using Mosaic.ViewModels;
using Mosaic.Views;

namespace Mosaic.Services;

public class DialogService : IDialogService
{
    private readonly IServiceProvider _services;

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    public string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select game executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select a folder to scan" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select cover image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public AddGameRequest? ShowAddGame()
    {
        var vm = _services.GetRequiredService<AddGameViewModel>();
        var window = new AddGameWindow { DataContext = vm, Owner = ActiveOwner() };
        return window.ShowDialog() == true ? vm.BuildRequest() : null;
    }

    public IReadOnlyList<ScanCandidate>? ShowScanResults(IReadOnlyList<ScanCandidate> candidates)
    {
        var vm = new ScanResultsViewModel(candidates);
        var window = new ScanResultsWindow { DataContext = vm, Owner = ActiveOwner() };
        return window.ShowDialog() == true ? vm.GetSelected() : null;
    }

    public IReadOnlyList<MediaScanCandidate>? ShowMediaScanResults(IReadOnlyList<MediaScanCandidate> candidates)
    {
        var vm = new MediaScanResultsViewModel(candidates);
        var window = new MediaScanResultsWindow { DataContext = vm, Owner = ActiveOwner() };
        return window.ShowDialog() == true ? vm.GetSelected() : null;
    }

    public void ShowGameDetail(int gameId)
    {
        var vm = _services.GetRequiredService<GameDetailViewModel>();
        var window = new GameDetailWindow { Owner = ActiveOwner() };
        vm.CloseRequested += () => window.Close();
        window.DataContext = vm;
        // Fire-and-forget load; the window binds as data arrives.
        _ = vm.InitializeAsync(gameId);
        window.ShowDialog();
    }

    public void ShowMediaDetail(int mediaItemId)
    {
        var vm = _services.GetRequiredService<MediaDetailViewModel>();
        var window = new MediaDetailWindow { Owner = ActiveOwner() };
        vm.CloseRequested += () => window.Close();
        window.DataContext = vm;
        _ = vm.InitializeAsync(mediaItemId);
        window.ShowDialog();
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public void ShowMessage(string message, string title) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static Window? ActiveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}
