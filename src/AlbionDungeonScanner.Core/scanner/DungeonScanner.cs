using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlbionDungeonScanner.Core.Avalonian;
using AlbionDungeonScanner.Core.Configuration;
using AlbionDungeonScanner.Core.Data;
using AlbionDungeonScanner.Core.Detection;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // Diperlukan untuk IOptions

namespace AlbionDungeonScanner.Core.scanner
{
    // Base class
    public class DungeonScanner : IDisposable
    {
        protected readonly ILogger<DungeonScanner> _logger;
        protected readonly NetworkCapture _networkCapture;
        protected readonly EntityDetector _entityDetector;
        protected readonly DataRepository _dataRepository;
        protected readonly ScannerConfiguration _configuration;

        private bool _isScanning;
        private List<DungeonEntity> _detectedEntities;

        public event Action<DungeonEntity> EntityDetected;
        public event Action<DungeonEntity> EntityRemoved;
        public event Action<string> StatusMessage;
        public event Action ScanStarted;
        public event Action ScanStopped;

        public bool IsScanning => _isScanning;
        public IReadOnlyList<DungeonEntity> DetectedEntities => _detectedEntities.AsReadOnly();

        // Constructor untuk DungeonScanner dasar
        public DungeonScanner(
            ILogger<DungeonScanner> logger,
            NetworkCapture networkCapture,
            EntityDetector entityDetector,
            DataRepository dataRepository,
            IOptions<ScannerConfiguration> configuration) // Gunakan IOptions untuk konfigurasi
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _networkCapture = networkCapture ?? throw new ArgumentNullException(nameof(networkCapture));
            _entityDetector = entityDetector ?? throw new ArgumentNullException(nameof(entityDetector));
            _dataRepository = dataRepository ?? throw new ArgumentNullException(nameof(dataRepository));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            
            _detectedEntities = new List<DungeonEntity>();
            SetupEventHandlers();
        }

        protected virtual void SetupEventHandlers()
        {
            _networkCapture.GameEventReceived += OnGameEventReceived;
            _networkCapture.StatusChanged += OnNetworkStatusChanged;
            _entityDetector.EntityDetectedEvent += OnEntityDetectedInternal;
            _entityDetector.EntityRemovedEvent += OnEntityRemovedInternal;
        }

        protected virtual void UnsubscribeEventHandlers()
        {
            _networkCapture.GameEventReceived -= OnGameEventReceived;
            _networkCapture.StatusChanged -= OnNetworkStatusChanged;
            _entityDetector.EntityDetectedEvent -= OnEntityDetectedInternal;
            _entityDetector.EntityRemovedEvent -= OnEntityRemovedInternal;
        }


        public virtual async Task StartScanAsync(string networkInterfaceName = "auto")
        {
            if (_isScanning)
            {
                _logger.LogWarning("Scan is already in progress.");
                StatusMessage?.Invoke("Scan already running.");
                return;
            }

            _logger.LogInformation("Attempting to start scan...");
            StatusMessage?.Invoke("Starting scan...");
            _detectedEntities.Clear();
            _entityDetector.ClearEntities(); // Bersihkan entitas di detector juga

            if (await Task.Run(() => _networkCapture.StartCapture(networkInterfaceName))) // Jalankan di thread lain agar UI tidak freeze
            {
                _isScanning = true;
                ScanStarted?.Invoke();
                _logger.LogInformation("Scan started successfully.");
                StatusMessage?.Invoke("Scan active.");
            }
            else
            {
                _logger.LogError("Failed to start network capture. Scan cannot start.");
                StatusMessage?.Invoke("Failed to start scan. Check network device and permissions.");
            }
        }

        public virtual async Task StopScanAsync()
        {
            if (!_isScanning)
            {
                _logger.LogWarning("Scan is not currently running.");
                StatusMessage?.Invoke("Scan not active.");
                return;
            }

            _logger.LogInformation("Stopping scan...");
            StatusMessage?.Invoke("Stopping scan...");
            await Task.Run(() => _networkCapture.StopCapture()); // Jalankan di thread lain
            _isScanning = false;
            ScanStopped?.Invoke();
            _logger.LogInformation("Scan stopped.");
            StatusMessage?.Invoke("Scan stopped.");
        }

        protected virtual void OnGameEventReceived(PhotonEvent photonEvent)
        {
            if (!_isScanning) return;
            
            // Log detail event jika logging verbose diaktifkan
            if (_logger.IsEnabled(LogLevel.Trace) && photonEvent != null)
            {
                 string eventName = _networkCapture.GetEventName(photonEvent.Code); // Asumsi GetEventName ada di NetworkCapture atau PhotonPacketParser
                 _logger.LogTrace("Scanner received game event. Code: {EventCode} ({EventName}), Params: {ParamCount}", 
                                 photonEvent.Code, eventName, photonEvent.Parameters?.Count ?? 0);
            }
            _entityDetector.ProcessEvent(photonEvent);
        }

        protected virtual void OnEntityDetectedInternal(DungeonEntity entity)
        {
            if (entity == null) return;

            var existingEntity = _detectedEntities.FirstOrDefault(e => e.Id == entity.Id);
            if (existingEntity == null)
            {
                _detectedEntities.Add(entity);
                EntityDetected?.Invoke(entity); // Picu event untuk GUI/plugin
                _logger.LogDebug("Entity added to scanner list: {EntityName} ({EntityType})", entity.Name, entity.Type);
            }
            else
            {
                // Update info jika perlu, misal posisi atau LastSeen
                existingEntity.Position = entity.Position;
                existingEntity.LastSeen = entity.LastSeen;
                // Pertimbangkan apakah akan memicu event update jika ada subscriber yang butuh
            }
        }

        protected virtual void OnEntityRemovedInternal(DungeonEntity entity)
        {
            if (entity == null) return;

            var removed = _detectedEntities.RemoveAll(e => e.Id == entity.Id) > 0;
            if (removed)
            {
                EntityRemoved?.Invoke(entity); // Picu event untuk GUI/plugin
                _logger.LogDebug("Entity removed from scanner list: {EntityName}", entity.Name);
            }
        }

        private void OnNetworkStatusChanged(string status)
        {
            StatusMessage?.Invoke($"Network: {status}");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeEventHandlers();
                if (_isScanning)
                {
                    Task.Run(async () => await StopScanAsync()).Wait(TimeSpan.FromSeconds(2)); // Beri sedikit waktu untuk stop
                }
                _networkCapture?.Dispose(); // Pastikan NetworkCapture IDisposable
            }
        }
    }

    // Enhanced scanner dengan fitur Avalonian
    public class EnhancedDungeonScanner : DungeonScanner
    {
        private readonly AvalonianDetector _avalonianDetector;
        private readonly ILogger<EnhancedDungeonScanner> _enhancedLogger; // Logger spesifik untuk Enhanced

        // Event ini sekarang ada dan akan digunakan oleh GUI
        public event Action<AvalonianScanResult> AvalonianScanResultUpdated; // Ganti nama agar lebih deskriptif
        
        public AvalonianDungeonMap CurrentAvalonianMap { get; private set; }

        public EnhancedDungeonScanner(
            ILogger<EnhancedDungeonScanner> enhancedLogger, // Logger khusus untuk ini
            NetworkCapture networkCapture,
            EntityDetector entityDetector,
            DataRepository dataRepository,
            IOptions<ScannerConfiguration> configuration, // Terima konfigurasi via IOptions
            AvalonianDetector avalonianDetector) // Inject AvalonianDetector
            : base(enhancedLogger, networkCapture, entityDetector, dataRepository, configuration) // Panggil base constructor
        {
            _enhancedLogger = enhancedLogger ?? throw new ArgumentNullException(nameof(enhancedLogger));
            _avalonianDetector = avalonianDetector ?? throw new ArgumentNullException(nameof(avalonianDetector));
        }

        protected override void OnEntityDetectedInternal(DungeonEntity entity)
        {
            base.OnEntityDetectedInternal(entity); // Panggil implementasi base class untuk deteksi umum

            if (entity == null) return;

            // Proses khusus untuk entitas Avalonian
            if (entity.DungeonType == DungeonType.Avalonian || _avalonianDetector.IsAvalonianEntity(entity.Name, entity.Id))
            {
                 _enhancedLogger.LogDebug("Processing Avalonian entity in EnhancedDungeonScanner: {EntityName}", entity.Name);
                ProcessAvalonianEntity(entity);
            }
        }

        private void ProcessAvalonianEntity(DungeonEntity entity)
        {
            try
            {
                var avalonianScanResult = _avalonianDetector.ProcessAvalonianEntity(entity.Id, entity.Position);
                if (avalonianScanResult != null)
                {
                    // Picu event yang bisa ditangkap oleh GUI atau plugin
                    AvalonianScanResultUpdated?.Invoke(avalonianScanResult);
                    _enhancedLogger.LogInformation("Avalonian scan result updated for {EntityName}.", avalonianScanResult.EntityData?.Name ?? entity.Name);

                    // Update map internal
                    CurrentAvalonianMap = _avalonianDetector.GenerateMap();
                    StatusMessage?.Invoke($"Avalonian Map Updated: {CurrentAvalonianMap?.Rooms?.Count ?? 0} rooms detected.");
                }
            }
            catch (Exception ex)
            {
                _enhancedLogger.LogError(ex, "Error processing Avalonian entity: {EntityName}", entity.Name);
            }
        }
        
        public async Task<AvalonianDungeonMap> GetCurrentAvalonianMapAsync()
        {
            // Pastikan GenerateMap tidak blocking atau jalankan di thread terpisah jika perlu
            return await Task.Run(() => _avalonianDetector.GenerateMap());
        }

        // Override StartScanAsync jika ada logika tambahan khusus untuk EnhancedScanner saat memulai
        public override async Task StartScanAsync(string networkInterfaceName = "auto")
        {
            _enhancedLogger.LogInformation("EnhancedDungeonScanner starting scan...");
            _avalonianDetector.Reset(); // Bersihkan state AvalonianDetector sebelum scan baru
            await base.StartScanAsync(networkInterfaceName);
            StatusMessage?.Invoke("Enhanced Scan active with Avalonian detection.");
        }

        // Override StopScanAsync jika ada logika tambahan
        public override async Task StopScanAsync()
        {
            _enhancedLogger.LogInformation("EnhancedDungeonScanner stopping scan...");
            await base.StopScanAsync();
             // Anda mungkin ingin men-generate laporan Avalonian terakhir di sini
            var finalMap = _avalonianDetector.GenerateMap();
            if (finalMap != null && finalMap.Rooms.Any())
            {
                 StatusMessage?.Invoke($"Final Avalonian map generated with {finalMap.Rooms.Count} rooms.");
            }
        }
    }
}