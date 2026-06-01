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

        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = mainVm };
        MainWindow = window;
        window.Show();
        await mainVm.InitializeAsync();
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

        services.AddHttpClient<SteamGridDbClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<SteamWebApiClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(30));

        // View models
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<RecentlyPlayedViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<GameDetailViewModel>();
        services.AddTransient<AddGameViewModel>();
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
