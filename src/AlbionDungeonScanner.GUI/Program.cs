using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Scanner;

namespace AlbionDungeonScanner.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                System.Console.WriteLine("Albion Dungeon Scanner - Console Mode");
                System.Console.WriteLine("====================================");

                var host = CreateHostBuilder(args).Build();
                
                // Start the scanner service
                var scanner = host.Services.GetRequiredService<DungeonScanner>();
                var logger = host.Services.GetRequiredService<ILogger<Program>>();

                logger.LogInformation("Starting Albion Dungeon Scanner in console mode");

                // Start scanner
                await scanner.StartAsync();

                System.Console.WriteLine("Scanner started. Press 'q' to quit...");
                
                // Wait for user input
                while (true)
                {
                    var key = System.Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                        break;
                    
                    if (key.KeyChar == 's' || key.KeyChar == 'S')
                    {
                        // Show statistics
                        ShowStatistics(scanner);
                    }
                }

                // Stop scanner
                await scanner.StopAsync();
                logger.LogInformation("Scanner stopped");
                
                await host.StopAsync();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(-1);
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Configure the same services as the GUI app
                    App.ConfigureServices(services, context.Configuration);
                })
                .UseConsoleLifetime();

        static void ShowStatistics(DungeonScanner scanner)
        {
            System.Console.Clear();
            System.Console.WriteLine("=== Scanner Statistics ===");
            
            // TODO: Display current scanner statistics
            System.Console.WriteLine($"Entities detected: {scanner.DetectedEntitiesCount}");
            System.Console.WriteLine($"Packets processed: {scanner.PacketsProcessedCount}");
            System.Console.WriteLine($"Scan duration: {scanner.ScanDuration}");
            
            System.Console.WriteLine("\nPress 's' for stats, 'q' to quit...");
        }
    }
}

// ================================================================
// Configuration Models
// ================================================================

namespace AlbionDungeonScanner.Core.Configuration
{
    public class ScannerConfiguration
    {
        public NetworkConfiguration Network { get; set; } = new();
        public DetectionConfiguration Detection { get; set; } = new();
        public AvalonianConfiguration Avalonian { get; set; } = new();
        public NotificationConfiguration Notifications { get; set; } = new();
        public DataSourceConfiguration DataSources { get; set; } = new();
        public LoggingConfiguration Logging { get; set; } = new();
        public PerformanceConfiguration Performance { get; set; } = new();
    }

    public class NetworkConfiguration
    {
        public string NetworkInterface { get; set; } = "auto";
        public int[] GamePorts { get; set; } = { 5055, 5056 };
        public int PacketBufferSize { get; set; } = 1024 * 1024; // 1MB
        public int MaxConcurrentPackets { get; set; } = 50;
        public bool UseAsyncProcessing { get; set; } = true;
    }

    public class DetectionConfiguration
    {
        public bool DetectChests { get; set; } = true;
        public bool DetectBosses { get; set; } = true;
        public bool DetectMobs { get; set; } = true;
        public bool DetectResources { get; set; } = false;
        public TimeSpan EntityCacheTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public int MinimumTierForNotification { get; set; } = 4;
    }

    public class AvalonianConfiguration
    {
        public bool PrioritizeAvalonianEntities { get; set; } = true;
        public bool ShowValueEstimates { get; set; } = true;
        public bool GenerateOptimalPaths { get; set; } = true;
        public int RoomSizeEstimate { get; set; } = 50;
        public ScanPriority PriorityThreshold { get; set; } = ScanPriority.High;
    }

    public class NotificationConfiguration
    {
        public bool EnableSounds { get; set; } = true;
        public int Volume { get; set; } = 50;
        public bool ShowDesktop { get; set; } = true;
        public bool NotifyHighValue { get; set; } = true;
        public bool NotifyBosses { get; set; } = true;
        public bool NotifyAvalonianEntities { get; set; } = true;
        public TimeSpan NotificationCooldown { get; set; } = TimeSpan.FromSeconds(5);
    }

    public class DataSourceConfiguration
    {
        public string MobsUrl { get; set; } = "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/mobs.json";
        public string ItemsUrl { get; set; } = "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/items.json";
        public string DungeonsUrl { get; set; } = "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/randomizeddungeons.json";
        public TimeSpan DataRefreshInterval { get; set; } = TimeSpan.FromHours(6);
        public bool AutoUpdateData { get; set; } = true;
    }

    public class LoggingConfiguration
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Information;
        public bool SaveToFile { get; set; } = true;
        public string LogPath { get; set; } = "./logs/";
        public int MaxLogFiles { get; set; } = 7;
        public long MaxLogFileSize { get; set; } = 50 * 1024 * 1024; // 50MB
    }

    public class PerformanceConfiguration
    {
        public bool EnablePerformanceMonitoring { get; set; } = true;
        public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromMinutes(1);
        public int MaxMemoryUsageMB { get; set; } = 512;
        public double MaxCpuUsagePercent { get; set; } = 80.0;
        public bool AutoOptimizePerformance { get; set; } = true;
    }

    // Configuration Manager Implementation
    public class ConfigurationManager
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConfigurationManager> _logger;
        private ScannerConfiguration _scannerConfig;
        private readonly string _configPath;

        public ConfigurationManager(IConfiguration configuration, ILogger<ConfigurationManager> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "scanner-settings.json");
            
            LoadConfiguration();
        }

        public ScannerConfiguration GetConfiguration()
        {
            return _scannerConfig ?? new ScannerConfiguration();
        }

        public void SaveConfiguration(ScannerConfiguration config)
        {
            try
            {
                _scannerConfig = config;
                
                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                var configDir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);
                
                File.WriteAllText(_configPath, json);
                
                _logger.LogInformation("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration");
                throw;
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                // Load from appsettings.json first
                _scannerConfig = new ScannerConfiguration();
                _configuration.GetSection("AlbionScanner").Bind(_scannerConfig);
                
                // Override with user settings if available
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var userConfig = System.Text.Json.JsonSerializer.Deserialize<ScannerConfiguration>(json);
                    if (userConfig != null)
                    {
                        _scannerConfig = userConfig;
                    }
                }
                
                _logger.LogInformation("Configuration loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration, using defaults");
                _scannerConfig = new ScannerConfiguration();
            }
        }

        public T GetSection<T>(string sectionName) where T : new()
        {
            var section = new T();
            _configuration.GetSection(sectionName).Bind(section);
            return section;
        }

        public void ReloadConfiguration()
        {
            LoadConfiguration();
        }
    }
}