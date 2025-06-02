using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AlbionDungeonScanner.Core.Configuration
{
    public class ConfigurationManager : INotifyPropertyChanged
    {
        private readonly ILogger<ConfigurationManager> _logger;
        private readonly string _configPath;
        private ScannerConfiguration _currentConfig;
        private readonly Dictionary<string, object> _configCache;
        private readonly FileSystemWatcher _configWatcher;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<ScannerConfiguration> ConfigurationChanged;

        public ScannerConfiguration Current => _currentConfig;

        public ConfigurationManager(ILogger<ConfigurationManager> logger = null)
        {
            _logger = logger;
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            _configCache = new Dictionary<string, object>();
            
            Directory.CreateDirectory(_configPath);
            InitializeConfiguration();
            SetupFileWatcher();
        }

        private void InitializeConfiguration()
        {
            var configFile = Path.Combine(_configPath, "scanner_config.json");
            
            if (File.Exists(configFile))
            {
                LoadConfiguration(configFile);
            }
            else
            {
                _currentConfig = ScannerConfiguration.Default;
                _ = Task.Run(SaveConfiguration);
            }
        }

        private void LoadConfiguration(string configFile)
        {
            try
            {
                var json = File.ReadAllText(configFile);
                _currentConfig = JsonConvert.DeserializeObject<ScannerConfiguration>(json) ?? ScannerConfiguration.Default;
                
                // Validate configuration
                var validationResults = ValidateConfiguration(_currentConfig);
                if (validationResults.Any())
                {
                    _logger?.LogWarning("Configuration validation errors: {Errors}", 
                        string.Join(", ", validationResults.Select(r => r.ErrorMessage)));
                    
                    // Use default for invalid settings
                    MergeWithDefaults();
                }

                _logger?.LogInformation("Configuration loaded successfully");
                OnConfigurationChanged();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load configuration, using defaults");
                _currentConfig = ScannerConfiguration.Default;
            }
        }

        public async Task SaveConfiguration()
        {
            try
            {
                var configFile = Path.Combine(_configPath, "scanner_config.json");
                var json = JsonConvert.SerializeObject(_currentConfig, Formatting.Indented);
                await File.WriteAllTextAsync(configFile, json);
                _logger?.LogInformation("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save configuration");
                throw;
            }
        }

        public void UpdateConfiguration(Action<ScannerConfiguration> updateAction)
        {
            updateAction(_currentConfig);
            OnConfigurationChanged();
            _ = Task.Run(SaveConfiguration); // Save asynchronously
        }

        public T GetSetting<T>(string key, T defaultValue = default)
        {
            if (_configCache.TryGetValue(key, out var cachedValue) && cachedValue is T)
            {
                return (T)cachedValue;
            }

            // Use reflection to get nested property values
            var value = GetNestedProperty(_currentConfig, key) ?? defaultValue;
            _configCache[key] = value;
            return (T)value;
        }

        public void SetSetting<T>(string key, T value)
        {
            SetNestedProperty(_currentConfig, key, value);
            _configCache[key] = value;
            OnConfigurationChanged();
        }

        private object GetNestedProperty(object obj, string propertyPath)
        {
            var properties = propertyPath.Split('.');
            var current = obj;

            foreach (var property in properties)
            {
                if (current == null) return null;
                
                var prop = current.GetType().GetProperty(property);
                if (prop == null) return null;
                
                current = prop.GetValue(current);
            }

            return current;
        }

        private void SetNestedProperty(object obj, string propertyPath, object value)
        {
            var properties = propertyPath.Split('.');
            var current = obj;

            for (int i = 0; i < properties.Length - 1; i++)
            {
                var prop = current.GetType().GetProperty(properties[i]);
                if (prop == null) return;
                
                current = prop.GetValue(current);
                if (current == null) return;
            }

            var finalProp = current.GetType().GetProperty(properties.Last());
            finalProp?.SetValue(current, value);
        }

        private List<ValidationResult> ValidateConfiguration(ScannerConfiguration config)
        {
            var context = new ValidationContext(config);
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(config, context, results, true);
            return results;
        }

        private void MergeWithDefaults()
        {
            var defaultConfig = ScannerConfiguration.Default;
            
            // Merge logic - use reflection to copy default values where current is invalid
            foreach (var prop in typeof(ScannerConfiguration).GetProperties())
            {
                var currentValue = prop.GetValue(_currentConfig);
                var defaultValue = prop.GetValue(defaultConfig);
                
                if (currentValue == null && defaultValue != null)
                {
                    prop.SetValue(_currentConfig, defaultValue);
                }
            }
        }

        private void SetupFileWatcher()
        {
            try
            {
                _configWatcher = new FileSystemWatcher(_configPath, "*.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _configWatcher.Changed += OnConfigFileChanged;
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to setup configuration file watcher");
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.Name == "scanner_config.json")
            {
                // Debounce file changes
                Task.Delay(1000).ContinueWith(_ => LoadConfiguration(e.FullPath));
            }
        }

        private void OnConfigurationChanged()
        {
            _configCache.Clear(); // Clear cache when config changes
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
            ConfigurationChanged?.Invoke(_currentConfig);
        }

        public void ResetToDefaults()
        {
            _currentConfig = ScannerConfiguration.Default;
            OnConfigurationChanged();
            _ = Task.Run(SaveConfiguration);
        }

        public void ImportConfiguration(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Configuration file not found");

            var json = File.ReadAllText(filePath);
            var importedConfig = JsonConvert.DeserializeObject<ScannerConfiguration>(json);
            
            if (importedConfig != null)
            {
                _currentConfig = importedConfig;
                OnConfigurationChanged();
                _ = Task.Run(SaveConfiguration);
            }
        }

        public void ExportConfiguration(string filePath)
        {
            var json = JsonConvert.SerializeObject(_currentConfig, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void Dispose()
        {
            _configWatcher?.Dispose();
        }
    }
}