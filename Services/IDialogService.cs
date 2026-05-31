using Mosaic.Models;

namespace Mosaic.Services;

public interface IDialogService
{
    /// <summary>Opens a file picker for an executable; returns the path or null if cancelled.</summary>
    string? PickExecutable();

    /// <summary>Opens a folder picker; returns the path or null if cancelled.</summary>
    string? PickFolder();

    /// <summary>Opens an image picker; returns the path or null if cancelled.</summary>
    string? PickImage();

    /// <summary>Shows the add-game dialog; returns the request or null if cancelled.</summary>
    AddGameRequest? ShowAddGame();

    /// <summary>Shows scan candidates for confirmation; returns the chosen subset or null if cancelled.</summary>
    IReadOnlyList<ScanCandidate>? ShowScanResults(IReadOnlyList<ScanCandidate> candidates);

    /// <summary>Opens the detail window for a game (modal).</summary>
    void ShowGameDetail(int gameId);

    bool Confirm(string message, string title);

    void ShowMessage(string message, string title);
}
