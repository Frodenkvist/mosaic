using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mosaic.Data;
using Mosaic.Services;
using Mosaic.ViewModels;

namespace Mosaic;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host not initialized.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureCreated();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => ConfigureServices(services, paths))
            .Build();

        await _host.StartAsync();

        // Apply EF Core migrations and clean up any session left open by a crash.
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MosaicDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
        }
        await _host.Services.GetRequiredService<IPlayTracker>().ReconcileOpenSessionsAsync();

        // Instantiate the Tier-1 watch observer so it subscribes to media playback (best-effort; no-op
        // when the system media-controls API publishes nothing for the user's player).
        _host.Services.GetRequiredService<SystemMediaWatchObserver>();

        // Instantiate the overlay service so it subscribes to play-session and achievement events
        // for the app lifetime (shows the in-game overlay + plays the achievement chime).
        _host.Services.GetRequiredService<AchievementOverlayService>();

        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        MainWindow = window;
        window.Show();
        await mainVm.InitializeAsync();

        // Best-effort background update check (no-op unless this is an installed build with
        // automatic checks enabled and the daily throttle elapsed). Never blocks startup.
        var updates = _host.Services.GetRequiredService<IUpdateService>();
        _ = Task.Run(async () =>
        {
            try { await updates.CheckForUpdateAsync(force: false); }
            catch { /* best-effort: a failed check must never disrupt the app */ }
        });
    }

    private static void ConfigureServices(IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);

        services.AddDbContextFactory<MosaicDbContext>(options =>
            options.UseSqlite($"Data Source={paths.DatabasePath};Cache=Shared"));

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IGameLibrary, GameLibrary>();
        services.AddSingleton<IPlayTracker, PlayTracker>();
        services.AddSingleton<IArtworkService, ArtworkService>();
        services.AddSingleton<IAchievementService, AchievementService>();
        services.AddSingleton<IDialogService, DialogService>();

        // In-game achievement overlay: a transparent, click-through window over launched games that
        // shows unlock toasts and plays a chime. The factory creates the WPF window (Views layer).
        services.AddSingleton<IAchievementOverlayFactory, Views.AchievementOverlayFactory>();
        services.AddSingleton<IAchievementSoundPlayer, AchievementSoundPlayer>();
        services.AddSingleton<AchievementOverlayService>();

        // Media domain (parallel to the game services; independent of them).
        services.AddSingleton<IMediaArtworkService, MediaArtworkService>();
        services.AddSingleton<IMediaLibrary, MediaLibrary>();
        services.AddSingleton<MediaPlaybackTracker>();
        services.AddSingleton<IMediaPlaybackTracker>(sp => sp.GetRequiredService<MediaPlaybackTracker>());
        services.AddSingleton<SystemMediaWatchObserver>();

        services.AddHttpClient<SteamGridDbClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<SteamWebApiClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<TmdbClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(30));

        // UpdateService is a typed HttpClient (GitHub needs a User-Agent; the timeout is generous
        // to cover the ~150 MB installer download). Exposed as a singleton so its UpdateAvailable
        // event and LastResult are shared across view models.
        services.AddHttpClient<UpdateService>(c =>
        {
            c.Timeout = TimeSpan.FromMinutes(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mosaic");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        services.AddSingleton<IUpdateService>(sp => sp.GetRequiredService<UpdateService>());

        // View models
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<RecentlyPlayedViewModel>();
        services.AddSingleton<MediaLibraryViewModel>();
        services.AddSingleton<MediaRecentlyWatchedViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<GameDetailViewModel>();
        services.AddTransient<AddGameViewModel>();
        services.AddTransient<MediaDetailViewModel>();
    }

    /// <summary>Runs an async action on the UI dispatcher thread.</summary>
    public static Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return action();
        return dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task.Unwrap();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
