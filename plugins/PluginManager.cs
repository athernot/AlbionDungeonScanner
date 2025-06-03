using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AlbionDungeonScanner.Core.Plugins
{
    // Plugin Interface
    public interface IPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }
        bool IsEnabled { get; set; }
        
        Task<bool> InitializeAsync(IServiceProvider serviceProvider, IPluginContext context);
        Task ShutdownAsync();
        Task<PluginConfigurationResult> ConfigureAsync(Dictionary<string, object> settings);
    }

    // Plugin Context for accessing scanner services
    public interface IPluginContext
    {
        event EventHandler<EntityDetectedEventArgs> EntityDetected;
        event EventHandler<AvalonianEntityEventArgs> AvalonianEntityDetected;
        event EventHandler<DungeonCompletedEventArgs> DungeonCompleted;
        
        Task SendNotificationAsync(string message, NotificationLevel level);
        Task<List<DungeonEntity>> GetCurrentEntitiesAsync();
        Task<AvalonianDungeonMap> GetAvalonianMapAsync();
        ILogger GetLogger(string categoryName);
    }

    // Plugin Events
    public class EntityDetectedEventArgs : EventArgs
    {
        public DungeonEntity Entity { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AvalonianEntityEventArgs : EventArgs
    {
        public AvalonianScanResult Result { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DungeonCompletedEventArgs : EventArgs
    {
        public DungeonType Type { get; set; }
        public TimeSpan Duration { get; set; }
        public int EntitiesFound { get; set; }
        public int TotalValue { get; set; }
    }

    // Plugin Configuration
    public class PluginConfiguration
    {
        public string PluginName { get; set; }
        public bool Enabled { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new Dictionary<string, object>();
        public DateTime LastModified { get; set; }
    }

    public class PluginConfigurationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> UpdatedSettings { get; set; }
    }

    public enum NotificationLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    // Plugin Manager
    public class PluginManager : IDisposable
    {
        private readonly List<IPlugin> _loadedPlugins;
        private readonly Dictionary<string, PluginConfiguration> _configurations;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PluginManager> _logger;
        private readonly PluginContext _pluginContext;
        private readonly string _pluginsDirectory;
        private readonly string _configPath;

        public PluginManager(IServiceProvider serviceProvider, ILogger<PluginManager> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _loadedPlugins = new List<IPlugin>();
            _configurations = new Dictionary<string, PluginConfiguration>();
            _pluginContext = new PluginContext(serviceProvider, logger);
            
            _pluginsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "plugins.json");
            
            EnsureDirectoriesExist();
        }

        private void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(_pluginsDirectory))
                Directory.CreateDirectory(_pluginsDirectory);
            
            var configDir = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);
        }

        public async Task LoadPluginsAsync()
        {
            try
            {
                _logger.LogInformation("Loading plugins from {PluginsDirectory}", _pluginsDirectory);
                
                await LoadConfigurationsAsync();
                
                var pluginFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.AllDirectories);
                
                foreach (var pluginFile in pluginFiles)
                {
                    await LoadPluginFromFileAsync(pluginFile);
                }

                _logger.LogInformation("Loaded {PluginCount} plugins", _loadedPlugins.Count);
                
                // Initialize enabled plugins
                foreach (var plugin in _loadedPlugins.Where(p => p.IsEnabled))
                {
                    await InitializePluginAsync(plugin);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading plugins");
            }
        }

        private async Task LoadPluginFromFileAsync(string filePath)
        {
            try
            {
                var assembly = Assembly.LoadFrom(filePath);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var pluginType in pluginTypes)
                {
                    var plugin = (IPlugin)Activator.CreateInstance(pluginType);
                    
                    // Load configuration
                    if (_configurations.ContainsKey(plugin.Name))
                    {
                        var config = _configurations[plugin.Name];
                        plugin.IsEnabled = config.Enabled;
                        await plugin.ConfigureAsync(config.Settings);
                    }
                    else
                    {
                        // Create default configuration
                        var defaultConfig = new PluginConfiguration
                        {
                            PluginName = plugin.Name,
                            Enabled = false, // Plugins start disabled by default
                            Settings = new Dictionary<string, object>(),
                            LastModified = DateTime.Now
                        };
                        _configurations[plugin.Name] = defaultConfig;
                    }
                    
                    _loadedPlugins.Add(plugin);
                    _logger.LogInformation("Loaded plugin: {PluginName} v{Version} by {Author}", 
                        plugin.Name, plugin.Version, plugin.Author);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading plugin from {FilePath}", filePath);
            }
        }

        private async Task InitializePluginAsync(IPlugin plugin)
        {
            try
            {
                _logger.LogInformation("Initializing plugin: {PluginName}", plugin.Name);
                
                var success = await plugin.InitializeAsync(_serviceProvider, _pluginContext);
                if (success)
                {
                    _logger.LogInformation("Successfully initialized plugin: {PluginName}", plugin.Name);
                }
                else
                {
                    _logger.LogWarning("Failed to initialize plugin: {PluginName}", plugin.Name);
                    plugin.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing plugin: {PluginName}", plugin.Name);
                plugin.IsEnabled = false;
            }
        }

        public async Task<bool> EnablePluginAsync(string pluginName)
        {
            var plugin = _loadedPlugins.FirstOrDefault(p => p.Name == pluginName);
            if (plugin == null)
            {
                _logger.LogWarning("Plugin not found: {PluginName}", pluginName);
                return false;
            }

            if (plugin.IsEnabled)
                return true;

            var success = await InitializePluginAsync(plugin);
            if (success)
            {
                plugin.IsEnabled = true;
                await UpdatePluginConfigurationAsync(pluginName, enabled: true);
            }

            return success;
        }

        public async Task<bool> DisablePluginAsync(string pluginName)
        {
            var plugin = _loadedPlugins.FirstOrDefault(p => p.Name == pluginName);
            if (plugin == null)
                return false;

            if (!plugin.IsEnabled)
                return true;

            try
            {
                await plugin.ShutdownAsync();
                plugin.IsEnabled = false;
                await UpdatePluginConfigurationAsync(pluginName, enabled: false);
                
                _logger.LogInformation("Disabled plugin: {PluginName}", pluginName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling plugin: {PluginName}", pluginName);
                return false;
            }
        }

        public async Task<PluginConfigurationResult> ConfigurePluginAsync(string pluginName, Dictionary<string, object> settings)
        {
            var plugin = _loadedPlugins.FirstOrDefault(p => p.Name == pluginName);
            if (plugin == null)
            {
                return new PluginConfigurationResult
                {
                    Success = false,
                    ErrorMessage = "Plugin not found"
                };
            }

            try
            {
                var result = await plugin.ConfigureAsync(settings);
                if (result.Success)
                {
                    // Update stored configuration
                    if (_configurations.ContainsKey(pluginName))
                    {
                        _configurations[pluginName].Settings = result.UpdatedSettings ?? settings;
                        _configurations[pluginName].LastModified = DateTime.Now;
                    }
                    
                    await SaveConfigurationsAsync();
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring plugin: {PluginName}", pluginName);
                return new PluginConfigurationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task UpdatePluginConfigurationAsync(string pluginName, bool enabled)
        {
            if (_configurations.ContainsKey(pluginName))
            {
                _configurations[pluginName].Enabled = enabled;
                _configurations[pluginName].LastModified = DateTime.Now;
                await SaveConfigurationsAsync();
            }
        }

        private async Task LoadConfigurationsAsync()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    var configs = JsonConvert.DeserializeObject<Dictionary<string, PluginConfiguration>>(json);
                    
                    foreach (var config in configs)
                    {
                        _configurations[config.Key] = config.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading plugin configurations");
            }
        }

        private async Task SaveConfigurationsAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_configurations, Formatting.Indented);
                await File.WriteAllTextAsync(_configPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving plugin configurations");
            }
        }

        public List<PluginInfo> GetPluginList()
        {
            return _loadedPlugins.Select(p => new PluginInfo
            {
                Name = p.Name,
                Version = p.Version,
                Author = p.Author,
                Description = p.Description,
                IsEnabled = p.IsEnabled,
                HasConfiguration = _configurations.ContainsKey(p.Name)
            }).ToList();
        }

        // Events to forward to plugins
        public void NotifyEntityDetected(DungeonEntity entity)
        {
            _pluginContext.TriggerEntityDetected(entity);
        }

        public void NotifyAvalonianEntityDetected(AvalonianScanResult result)
        {
            _pluginContext.TriggerAvalonianEntityDetected(result);
        }

        public void NotifyDungeonCompleted(DungeonType type, TimeSpan duration, int entitiesFound, int totalValue)
        {
            _pluginContext.TriggerDungeonCompleted(type, duration, entitiesFound, totalValue);
        }

        public void Dispose()
        {
            foreach (var plugin in _loadedPlugins.Where(p => p.IsEnabled))
            {
                try
                {
                    plugin.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error shutting down plugin: {PluginName}", plugin.Name);
                }
            }
            
            _loadedPlugins.Clear();
            _configurations.Clear();
        }
    }

    // Plugin Context Implementation
    public class PluginContext : IPluginContext
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public event EventHandler<EntityDetectedEventArgs> EntityDetected;
        public event EventHandler<AvalonianEntityEventArgs> AvalonianEntityDetected;
        public event EventHandler<DungeonCompletedEventArgs> DungeonCompleted;

        public PluginContext(IServiceProvider serviceProvider, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task SendNotificationAsync(string message, NotificationLevel level)
        {
            try
            {
                var notificationManager = _serviceProvider.GetService<INotificationManager>();
                await notificationManager?.SendNotificationAsync(message, level);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification: {Message}", message);
            }
        }

        public async Task<List<DungeonEntity>> GetCurrentEntitiesAsync()
        {
            try
            {
                var entityDetector = _serviceProvider.GetService<EntityDetector>();
                return entityDetector?.GetCurrentEntities() ?? new List<DungeonEntity>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current entities");
                return new List<DungeonEntity>();
            }
        }

        public async Task<AvalonianDungeonMap> GetAvalonianMapAsync()
        {
            try
            {
                var avalonianDetector = _serviceProvider.GetService<AvalonianDetector>();
                return avalonianDetector?.GenerateMap();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Avalonian map");
                return null;
            }
        }

        public ILogger GetLogger(string categoryName)
        {
            var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
            return loggerFactory?.CreateLogger(categoryName) ?? _logger;
        }

        internal void TriggerEntityDetected(DungeonEntity entity)
        {
            EntityDetected?.Invoke(this, new EntityDetectedEventArgs
            {
                Entity = entity,
                Timestamp = DateTime.Now
            });
        }

        internal void TriggerAvalonianEntityDetected(AvalonianScanResult result)
        {
            AvalonianEntityDetected?.Invoke(this, new AvalonianEntityEventArgs
            {
                Result = result,
                Timestamp = DateTime.Now
            });
        }

        internal void TriggerDungeonCompleted(DungeonType type, TimeSpan duration, int entitiesFound, int totalValue)
        {
            DungeonCompleted?.Invoke(this, new DungeonCompletedEventArgs
            {
                Type = type,
                Duration = duration,
                EntitiesFound = entitiesFound,
                TotalValue = totalValue
            });
        }
    }

    // Plugin Info for UI display
    public class PluginInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; }
        public bool HasConfiguration { get; set; }
    }

    // Interface for notification manager (to be implemented by core)
    public interface INotificationManager
    {
        Task SendNotificationAsync(string message, NotificationLevel level);
    }
}