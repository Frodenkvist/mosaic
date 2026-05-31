namespace Mosaic.Services;

/// <summary>Thrown when adding a game whose executable path is already in the library.</summary>
public class DuplicateExecutableException : Exception
{
    public string ExecutablePath { get; }

    public DuplicateExecutableException(string executablePath)
        : base($"A game with executable '{executablePath}' already exists.")
    {
        ExecutablePath = executablePath;
    }
}
