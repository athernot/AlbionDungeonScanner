using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Network;
using AlbionDungeonScanner.Core.Detection;
using AlbionDungeonScanner.Core.Data;
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Scanner
{
    public class DungeonScanner
    {
        protected readonly NetworkCapture _networkCapture;
        protected readonly EntityDetector _entityDetector;
        protected readonly DataRepository _dataRepository;
        protected readonly ILogger<DungeonScanner> _logger;
        protected bool _isScanning;

        public event Action<DungeonEntity> OnEntityDetected;
        public event Action<DungeonEntity> OnEntityRemoved;
        public event Action<bool> OnScanningStatusChanged;
        public event Action<string> OnStatusMessage;

        public bool IsScanning => _isScanning;

        public DungeonScanner(ILogger<DungeonScanner> logger = null)
        {
            _logger = logger;
            _dataRepository = new DataRepository(logger);
            _entityDetector = new EntityDetector(logger);
            _networkCapture = new NetworkCapture(logger);

            SetupEventHandlers();
        }

        private void SetupEventHandlers()
        {
            // Network capture events
            _networkCapture.PacketReceived += OnPacketReceived;
            _networkCapture.StatusChanged += OnNetworkStatusChanged;

            // Entity detector events
            _entityDetector.OnEntityDetected += OnEntityDetectedInternal;
            _entityDetector.OnEntityRemoved += OnEntityRemovedInternal;
        }

        public virtual async Task<bool> StartAsync()
        {
            try
            {
                _logger?.LogInformation("Starting dungeon scanner...");
                OnStatusMessage?.Invoke("Starting scanner...");

                // Wait for data to load
                var timeout = DateTime.UtcNow.AddSeconds(30);
                while (!_dataRepository.IsDataLoaded() && DateTime.UtcNow < timeout)
                {
                    await Task.Delay(1000);
                    OnStatusMessage?.Invoke("Loading game data...");
                }

                if (!_dataRepository.IsDataLoaded())
                {
                    _logger?.LogWarning("Game data not fully loaded, continuing with fallback data");
                }

                // Start network capture
                var captureStarted = _networkCapture.StartCapture();
                if (!captureStarted)
                {
                    OnStatusMessage?.Invoke("Failed to start network capture");
                    return false;
                }

                _isScanning = true;
                OnScanningStatusChanged?.Invoke(true);
                OnStatusMessage?.Invoke("Scanner active - monitoring dungeon entities");

                _logger?.LogInformation("Dungeon scanner started successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start dungeon scanner");
                OnStatusMessage?.Invoke($"Scanner start failed: {ex.Message}");
                return false;
            }
        }

        public virtual async Task StopAsync()
        {
            try
            {
                _logger?.LogInformation("Stopping dungeon scanner...");
                OnStatusMessage?.Invoke("Stopping scanner...");

                _networkCapture.StopCapture();
                _entityDetector.ClearEntities();

                _isScanning = false;
                OnScanningStatusChanged?.Invoke(false);
                OnStatusMessage?.Invoke("Scanner stopped");

                _logger?.LogInformation("Dungeon scanner stopped");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping dungeon scanner");
                OnStatusMessage?.Invoke($"Error stopping scanner: {ex.Message}");
            }
        }

        private void OnPacketReceived(PhotonEvent photonEvent)
        {
            try
            {
                _entityDetector.ProcessEvent(photonEvent);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"Error processing packet: {ex.Message}");
            }
        }

        private void OnNetworkStatusChanged(string status)
        {
            OnStatusMessage?.Invoke($"Network: {status}");
        }

        protected virtual void OnEntityDetectedInternal(DungeonEntity entity)
        {
            try
            {
                _logger?.LogDebug($"Entity detected: {entity.Type} - {entity.Name} at {entity.Position}");
                
                // Special handling for high-value entities
                if (IsHighValueEntity(entity))
                {
                    _logger?.LogInformation($"High-value entity detected: {entity.Name}");
                    OnStatusMessage?.Invoke($"⭐ High-value {entity.Type}: {entity.Name}");
                }

                OnEntityDetected?.Invoke(entity);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling entity detection");
            }
        }

        protected virtual void OnEntityRemovedInternal(DungeonEntity entity)
        {
            try
            {
                _logger?.LogDebug($"Entity removed: {entity.Type} - {entity.Name}");
                OnEntityRemoved?.Invoke(entity);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling entity removal");
            }
        }

        private bool IsHighValueEntity(DungeonEntity entity)
        {
            // Define high-value criteria
            switch (entity.Type)
            {
                case EntityType.Boss:
                    return true; // All bosses are high-value

                case EntityType.Chest:
                    return entity.Name.Contains("T6") || 
                           entity.Name.Contains("T7") || 
                           entity.Name.Contains("T8") ||
                           entity.Name.Contains("Avalonian");

                case EntityType.ResourceNode:
                    return entity.Name.Contains("Avalonian") ||
                           entity.Name.Contains("T6") ||
                           entity.Name.Contains("T7") ||
                           entity.Name.Contains("T8");

                default:
                    return false;
            }
        }

        public List<DungeonEntity> GetCurrentEntities()
        {
            return _entityDetector.GetCurrentEntities();
        }

        public Dictionary<EntityType, int> GetEntityCounts()
        {
            var entities = _entityDetector.GetCurrentEntities();
            var counts = new Dictionary<EntityType, int>();

            foreach (EntityType type in Enum.GetValues<EntityType>())
            {
                counts[type] = entities.Count(e => e.Type == type);
            }

            return counts;
        }

        public List<string> GetAvailableNetworkInterfaces()
        {
            return _networkCapture.GetAvailableInterfaces();
        }

        public async Task RefreshGameDataAsync()
        {
            try
            {
                OnStatusMessage?.Invoke("Refreshing game data...");
                await _dataRepository.RefreshDataAsync();
                OnStatusMessage?.Invoke("Game data refreshed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error refreshing game data");
                OnStatusMessage?.Invoke("Failed to refresh game data");
            }
        }

        public Dictionary<string, object> GetScannerStatistics()
        {
            var entities = _entityDetector.GetCurrentEntities();
            var dataStats = _dataRepository.GetDataStatistics();

            return new Dictionary<string, object>
            {
                ["IsScanning"] = _isScanning,
                ["TotalEntities"] = entities.Count,
                ["EntityCounts"] = GetEntityCounts(),
                ["AvalonianEntities"] = entities.Count(e => e.DungeonType == DungeonType.Avalonian),
                ["DataLoadTime"] = _dataRepository.GetLastDataLoadTime(),
                ["LoadedMobs"] = dataStats["Mobs"],
                ["LoadedChests"] = dataStats["Chests"],
                ["LoadedItems"] = dataStats["Items"]
            };
        }

        public virtual void Dispose()
        {
            try
            {
                _ = StopAsync();
                _networkCapture?.Dispose();
                _dataRepository?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disposing scanner");
            }
        }
    }

    // Enhanced scanner dengan fitur tambahan
    public class EnhancedDungeonScanner : DungeonScanner
    {
        public event Action<AvalonianScanResult> OnAvalonianEntityDetected;
        
        public AvalonianDungeonMap CurrentAvalonianMap { get; private set; }

        public EnhancedDungeonScanner(ILogger<EnhancedDungeonScanner> logger = null) : base(logger)
        {
            // Enhanced scanner akan dikembangkan lebih lanjut dengan Avalonian detector
        }

        protected override void OnEntityDetectedInternal(DungeonEntity entity)
        {
            base.OnEntityDetectedInternal(entity);

            // Special processing untuk Avalonian entities
            if (entity.DungeonType == DungeonType.Avalonian)
            {
                ProcessAvalonianEntity(entity);
            }
        }

        private void ProcessAvalonianEntity(DungeonEntity entity)
        {
            try
            {
                // Ini akan diintegrasikan dengan AvalonianDetector nanti
                _logger?.LogInformation($"Avalonian entity detected: {entity.Name}");
                
                // Placeholder untuk Avalonian processing
                // Will be enhanced when AvalonianDetector is integrated
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing Avalonian entity");
            }
        }

        public void UpdateAvalonianMap(AvalonianDungeonMap map)
        {
            CurrentAvalonianMap = map;
        }
    }
}