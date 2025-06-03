using System;
using System.IO;
using AlbionDungeonScanner.Core.Analytics;
using AlbionDungeonScanner.Core.Avalonian;
using AlbionDungeonScanner.Core.Configuration;
using AlbionDungeonScanner.Core.Data;
using AlbionDungeonScanner.Core.Detection;
using AlbionDungeonScanner.Core.Market;
using AlbionDungeonScanner.Core.Network;
using AlbionDungeonScanner.Core.Performance;
using AlbionDungeonScanner.Core.scanner;
using AlbionDungeonScanner.Core.Services;
using AlbionDungeonScanner.GUI.Services; // Asumsi ada namespace ini untuk background services
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Windows; // Diperlukan untuk kelas Application

namespace AlbionDungeonScanner.GUI
{
    public partial class App : Application
    {
        private IHost _host;
        private static IServiceProvider _serviceProvider; // Agar bisa diakses statis jika perlu

        public App()
        {
            // Pastikan Serilog dikonfigurasi sebelum host dibangun jika log diperlukan selama startup awal
            var preliminaryConfig = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(preliminaryConfig)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("logs/albiondungeonscanner-.txt", rollingInterval: RollingInterval.Day)
                .CreateBootstrapLogger(); // Logger awal sebelum DI
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            _host = CreateHostBuilder(e.Args).Build();
            _serviceProvider = _host.Services; // Set static service provider

            await _host.StartAsync();

            // Setup global exception handler
            SetupGlobalExceptionHandling();


            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            var configManager = _serviceProvider.GetRequiredService<ConfigurationManager>();
            configManager.StartMonitoring(); // Mulai monitoring konfigurasi

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
            }
            Log.CloseAndFlush(); // Pastikan semua log di-flush
            base.OnExit(e);
        }
        
        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    if (args != null)
                    {
                        config.AddCommandLine(args);
                    }
                })
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services, context.Configuration);
                })
                .UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services) // Memungkinkan service lain untuk mempengaruhi konfigurasi Serilog
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File("logs/albiondungeonscanner-.txt",
                                  rollingInterval: RollingInterval.Day,
                                  outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

        // Direvisi untuk memastikan semua dependensi terdaftar dengan benar
        internal static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
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
            
            // Daftarkan EnhancedDungeonScanner sebagai implementasi utama untuk dirinya sendiri dan interface/base class jika ada
            services.AddSingleton<EnhancedDungeonScanner>();
            // Jika EnhancedDungeonScanner adalah implementasi dari IDungeonScanner atau DungeonScanner (base class)
            // dan Anda ingin bisa me-resolve DungeonScanner dan mendapatkan EnhancedDungeonScanner:
            services.AddSingleton<DungeonScanner>(provider => provider.GetRequiredService<EnhancedDungeonScanner>());
            // Jika ada interface IDungeonScanner:
            // services.AddSingleton<IDungeonScanner>(provider => provider.GetRequiredService<EnhancedDungeonScanner>());


            // Advanced Services
            services.AddSingleton<ScannerAnalytics>();
            // services.AddSingleton<PatternRecognitionEngine>(); // Implementasi jika ada
            services.AddSingleton<PerformanceManager>();

            // Business Services
            services.AddSingleton<DataPersistenceService>();
            services.AddSingleton<NotificationManager>(); // Jika ini implementasi INotificationManager
            // Jika ada interface INotificationManager:
            // services.AddSingleton<INotificationManager, NotificationManager>();
            services.AddSingleton<MarketDataService>();
            services.AddSingleton<DataExportService>();


            // Plugin System
            services.AddSingleton<PluginManager>();

            // GUI Services
            // MainWindow dan window lain biasanya Transient atau Scoped, bukan Singleton
            services.AddTransient<MainWindow>();
            services.AddTransient<SettingsWindow>();
            services.AddTransient<PluginsWindow>();

            // HTTP Client Factory untuk Albion Data Project atau API lain
            services.AddHttpClient("AlbionData", client =>
            {
                client.BaseAddress = new Uri(configuration.GetValue<string>("AlbionDataApi:BaseUrl", "https://www.albion-online-data.com/api/v2/"));
                client.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int>("HttpClientSettings:TimeoutSeconds", 30));
                client.DefaultRequestHeaders.Add("User-Agent", configuration.GetValue<string>("HttpClientSettings:UserAgent", "AlbionDungeonScanner/1.0"));
            });
            
            // Jika DataRepository memerlukan IHttpClientFactory
            services.AddHttpClient<DataRepository>(); // Ini akan meng-inject HttpClient ke DataRepository jika constructornya menerimanya


            // Background Services (opsional, tergantung kebutuhan)
            // services.AddHostedService<DataUpdateService>(); // Contoh jika ada service update data periodik
            // services.AddHostedService<PerformanceMonitoringService>(); // Contoh
        }

        private void SetupGlobalExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogUnhandledException((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");
            };

            DispatcherUnhandledException += (s, e) =>
            {
                LogUnhandledException(e.Exception, "Application.Current.DispatcherUnhandledException");
                e.Handled = true; // Mencegah aplikasi crash, tapi pastikan error ditangani/dilaporkan
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
                e.SetObserved(); // Mencegah aplikasi crash
            };
        }

        private void LogUnhandledException(Exception exception, string source)
        {
            string message = $"Unhandled exception ({source})";
            try
            {
                System.Reflection.AssemblyName assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName();
                message = $"Unhandled exception in {assemblyName.Name} v{assemblyName.Version}";
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Exception in LogUnhandledException");
            }
            finally
            {
                Log.Logger.Fatal(exception, message);
                // Pertimbangkan untuk menampilkan dialog error ke pengguna di sini
                // MessageBox.Show("An unexpected error occurred. Please check the logs for more details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Contoh Background Services jika Anda membutuhkannya:
    // (Pindahkan ke file terpisah jika sudah kompleks)
    public class DataUpdateService : BackgroundService
    {
        private readonly ILogger<DataUpdateService> _logger;
        private readonly DataRepository _dataRepository;

        public DataUpdateService(ILogger<DataUpdateService> logger, DataRepository dataRepository)
        {
            _logger = logger;
            _dataRepository = dataRepository;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DataUpdateService running at: {time}", DateTimeOffset.Now);
                // await _dataRepository.RefreshDataSourcesAsync(); // Contoh pemanggilan
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken); // Update setiap 6 jam
            }
        }
    }
}