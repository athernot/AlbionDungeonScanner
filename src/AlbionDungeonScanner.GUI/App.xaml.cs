<Application x:Class="AlbionDungeonScanner.GUI.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <!-- Global Application Resources -->
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/DarkTheme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
*/

// App.xaml.cs
using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Scanner;
using AlbionDungeonScanner.Core.Network;
using AlbionDungeonScanner.Core.Detection;
using AlbionDungeonScanner.Core.Data;
using AlbionDungeonScanner.Core.Avalonian;
using AlbionDungeonScanner.Core.Configuration;
using AlbionDungeonScanner.Core.Plugins;
using AlbionDungeonScanner.Core.Services;
using AlbionDungeonScanner.Core.Analytics;
using AlbionDungeonScanner.Core.Performance;

namespace AlbionDungeonScanner.GUI
{
    public partial class App : Application
    {
        private IHost _host;
        private IServiceProvider _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Configure Serilog
                ConfigureSerilog();

                // Build host with dependency injection
                _host = CreateHostBuilder(e.Args).Build();
                _serviceProvider = _host.Services;

                // Start host services
                await _host.StartAsync();

                // Create and show main window
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application startup failed: {ex.Message}", 
                               "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown(-1);
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_host != null)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                    _host.Dispose();
                }

                Log.CloseAndFlush();
            }
            catch (Exception ex)
            {
                // Log to Windows Event Log if Serilog fails
                System.Diagnostics.EventLog.WriteEntry("Application", 
                    $"Error during shutdown: {ex.Message}", 
                    System.Diagnostics.EventLogEntryType.Error);
            }

            base.OnExit(e);
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    var basePath = AppDomain.CurrentDomain.BaseDirectory;
                    
                    config.SetBasePath(basePath)
                          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", 
                                     optional: true, reloadOnChange: true)
                          .AddEnvironmentVariables()
                          .AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services, context.Configuration);
                })
                .UseSerilog();

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Configuration
            services.Configure<ScannerConfiguration>(configuration.GetSection("AlbionScanner"));
            services.AddSingleton<ConfigurationManager>();

            // Core Services
            services.AddSingleton<DataRepository>();
            services.AddSingleton<PhotonPacketParser>();
            services.AddSingleton<NetworkCapture>();
            services.AddSingleton<EntityDetector>();
            services.AddSingleton<AvalonianDetector>();
            services.AddSingleton<DungeonScanner>();

            // Advanced Services
            services.AddSingleton<ScannerAnalytics>();
            services.AddSingleton<PatternRecognitionEngine>();
            services.AddSingleton<PerformanceManager>();

            // Business Services
            services.AddSingleton<DataPersistenceService>();
            services.AddSingleton<NotificationManager>();
            services.AddSingleton<INotificationManager>(provider => 
                provider.GetService<NotificationManager>());

            // Plugin System
            services.AddSingleton<PluginManager>();

            // GUI Services
            services.AddTransient<MainWindow>();
            services.AddTransient<SettingsWindow>();
            services.AddTransient<PluginsWindow>();

            // HTTP Client for data fetching
            services.AddHttpClient("AlbionData", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", 
                    "AlbionDungeonScanner/1.0 (https://github.com/user/albion-scanner)");
            });

            // Background Services
            services.AddHostedService<DataUpdateService>();
            services.AddHostedService<PerformanceMonitoringService>();
        }

        private static void ConfigureSerilog()
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProcessId()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}")
                .WriteTo.File(
                    path: Path.Combine(logPath, "scanner-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    fileSizeLimitBytes: 50 * 1024 * 1024, // 50MB
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj} {NewLine}{Exception}")
                .CreateLogger();
        }
    }

    // Background Services
    public class DataUpdateService : BackgroundService
    {
        private readonly ILogger<DataUpdateService> _logger;
        private readonly DataRepository _dataRepository;
        private readonly IConfiguration _configuration;

        public DataUpdateService(
            ILogger<DataUpdateService> logger,
            DataRepository dataRepository,
            IConfiguration configuration)
        {
            _logger = logger;
            _dataRepository = dataRepository;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var updateInterval = TimeSpan.FromHours(6); // Update every 6 hours
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Checking for data updates");
                    await _dataRepository.RefreshDataAsync();
                    _logger.LogInformation("Data update completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during data update");
                }

                await Task.Delay(updateInterval, stoppingToken);
            }
        }
    }

    public class PerformanceMonitoringService : BackgroundService
    {
        private readonly ILogger<PerformanceMonitoringService> _logger;
        private readonly PerformanceManager _performanceManager;

        public PerformanceMonitoringService(
            ILogger<PerformanceMonitoringService> logger,
            PerformanceManager performanceManager)
        {
            _logger = logger;
            _performanceManager = performanceManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var monitorInterval = TimeSpan.FromMinutes(1);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _performanceManager.CollectMetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during performance monitoring");
                }

                await Task.Delay(monitorInterval, stoppingToken);
            }
        }
    }
}