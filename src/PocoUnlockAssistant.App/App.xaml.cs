using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PocoUnlockAssistant.App.ViewModels;
using PocoUnlockAssistant.Core.Abstractions;
using PocoUnlockAssistant.Core.Automation;
using PocoUnlockAssistant.Core.Configuration;
using PocoUnlockAssistant.Core.Scheduling;
using PocoUnlockAssistant.Core.Time;
using PocoUnlockAssistant.Infrastructure.Adb;
using PocoUnlockAssistant.Infrastructure.Configuration;
using PocoUnlockAssistant.Infrastructure.Logging;
using PocoUnlockAssistant.Infrastructure.Persistence;
using PocoUnlockAssistant.Infrastructure.Time;

namespace PocoUnlockAssistant.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();

        var builder = Host.CreateApplicationBuilder(e.Args);
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogsDirectory));

        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().Load());
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().LoadScreenProfile());

        builder.Services.AddSingleton<INtpClient>(sp =>
        {
            var settings = sp.GetRequiredService<AssistantSettings>();
            return new NtpClient(settings.NtpServers, logger: sp.GetRequiredService<ILogger<NtpClient>>());
        });

        builder.Services.AddSingleton<IAdbClient>(sp =>
        {
            var settings = sp.GetRequiredService<AssistantSettings>();
            return new AdbClient(settings.AdbPath, sp.GetRequiredService<ILogger<AdbClient>>());
        });

        builder.Services.AddSingleton<IScreenInspector>(sp => new UiautomatorScreenInspector(
            sp.GetRequiredService<IAdbClient>(),
            AppPaths.EvidenceDirectory,
            sp.GetRequiredService<ILogger<UiautomatorScreenInspector>>()));

        builder.Services.AddSingleton<IAttemptStore>(sp =>
        {
            var store = new SqliteAttemptStore(AppPaths.DatabaseFile);
            store.InitializeAsync().GetAwaiter().GetResult();
            return store;
        });

        builder.Services.AddSingleton(_ => new MonotonicClock(DateTimeOffset.UtcNow));

        builder.Services.AddSingleton(sp => new SubmissionCoordinator(
            sp.GetRequiredService<INtpClient>(),
            sp.GetRequiredService<IAdbClient>(),
            sp.GetRequiredService<IScreenInspector>(),
            sp.GetRequiredService<IAttemptStore>(),
            sp.GetRequiredService<AssistantSettings>(),
            sp.GetRequiredService<ScreenRecognitionProfile>(),
            sp.GetRequiredService<MonotonicClock>()));

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        DispatcherUnhandledException += (_, args) =>
        {
            _host.Services.GetRequiredService<ILogger<App>>()
                .LogError(args.Exception, "Unhandled dispatcher exception.");
            MessageBox.Show(
                args.Exception.Message,
                "POCO Unlock Timing Assistant",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
