using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mosaic.Services;

namespace Mosaic.ViewModels;

public partial class AddGameViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _executablePath = string.Empty;
    [ObservableProperty] private string? _launchArguments;
    [ObservableProperty] private string? _workingDirectory;
    [ObservableProperty] private string? _realExecutableName;

    public AddGameViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    public bool IsValid => !string.IsNullOrWhiteSpace(ExecutablePath);

    [RelayCommand]
    private void BrowseExecutable()
    {
        var path = _dialogs.PickExecutable();
        if (string.IsNullOrWhiteSpace(path))
            return;
        ExecutablePath = path;
        if (string.IsNullOrWhiteSpace(Name))
            Name = Path.GetFileNameWithoutExtension(path);
    }

    public AddGameRequest? BuildRequest()
    {
        if (!IsValid)
            return null;
        return new AddGameRequest(
            Name,
            ExecutablePath,
            LaunchArguments,
            WorkingDirectory,
            RealExecutableName);
    }
}
