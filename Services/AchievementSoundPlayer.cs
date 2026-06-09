using System.Media;
using System.Windows;

namespace Mosaic.Services;

/// <summary>
/// Plays the bundled "achievement unlocked" chime. The WAV is loaded once from the application
/// resource stream into a <see cref="SoundPlayer"/>; <see cref="Play"/> is asynchronous (it does
/// not block the UI). Best-effort throughout — a missing/invalid asset or playback failure simply
/// means no sound, never an exception.
/// </summary>
public class AchievementSoundPlayer : IAchievementSoundPlayer
{
    private readonly SoundPlayer? _player;

    public AchievementSoundPlayer()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/achievement.wav", UriKind.Absolute);
            var resource = Application.GetResourceStream(uri);
            if (resource is not null)
            {
                _player = new SoundPlayer(resource.Stream);
                _player.Load();   // read fully now so Play() just plays the cached buffer
            }
        }
        catch
        {
            _player = null;
        }
    }

    public void Play()
    {
        try
        {
            _player?.Play();   // plays on a worker thread; non-blocking
        }
        catch
        {
            // never let a sound failure disrupt an unlock
        }
    }
}
