using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AlbionDungeonScanner.Core.Models;

namespace AlbionDungeonScanner.Core.Configuration
{
    public class ScannerConfiguration
    {
        [Required]
        public NetworkConfiguration Network { get; set; } = new();
        
        [Required]
        public DetectionConfiguration Detection { get; set; } = new();
        
        [Required]
        public AvalonianConfiguration Avalonian { get; set; } = new();
        
        [Required]
        public NotificationConfiguration Notifications { get; set; } = new();
        
        [Required]
        public PerformanceConfiguration Performance { get; set; } = new();
        
        [Required]
        public UIConfiguration UI { get; set; } = new();
        
        public DatabaseConfiguration Database { get; set; } = new();
        
        public PluginConfiguration Plugins { get; set; } = new();

        public static ScannerConfiguration Default => new()
        {
            Network = new NetworkConfiguration
            {
                AutoDetectInterface = true,
                GamePorts = new[] { 5055, 5056 },
                PacketBufferSize = 1024 * 1024,
                MaxConcurrentPackets = 100,
                FilteringEnabled = true,
                ProtocolVersion = "1.6",
                ConnectionTimeout = TimeSpan.FromSeconds(30)
            },
            Detection = new DetectionConfiguration
            {
                EnabledEntityTypes = new[] { EntityType.Chest, EntityType.Boss, EntityType.Mob },
                MinimumTier = 4,
                IgnoreCommonMobs = true,
                DetectionRadius = 1000,
                CacheTimeout = TimeSpan.FromMinutes(30),
                EnablePredictiveDetection = true,
                BlacklistedEntities = new string[0],
                TypePriorities = new Dictionary<EntityType, ScanPriority>
                {
                    [EntityType.Boss] = ScanPriority.Critical,
                    [EntityType.Chest] = ScanPriority.High,
                    [EntityType.Mob] = ScanPriority.Medium,
                    [EntityType.ResourceNode] = ScanPriority.Medium
                }
            },
            Avalonian = new AvalonianConfiguration
            {
                PriorityThreshold = ScanPriority.High,
                ShowEstimatedValues = true,
                GenerateOptimalPaths = true,
                RoomSizeEstimate = 50,
                EnableRoomMapping = true,
                PathOptimizationAlgorithm = "AStar",
                EnableThreatAssessment = true,
                ShowRecommendedStrategies = true
            },
            Notifications = new NotificationConfiguration
            {
                EnableDesktopNotifications = true,
                EnableSoundAlerts = true,
                MinimumPriority = NotificationPriority.High,
                Discord = new DiscordNotificationConfig
                {
                    Enabled = false,
                    WebhookUrl = "",
                    MinPriority = NotificationPriority.High,
                    Username = "Albion Scanner",
                    AvatarUrl = ""
                },
                Email = new EmailNotificationConfig
                {
                    Enabled = false,
                    SmtpServer = "",
                    SmtpPort = 587,
                    UseSSL = true
                },
                TypeSettings = new Dictionary<NotificationType, bool>
                {
                    [NotificationType.HighValueTarget] = true,
                    [NotificationType.Boss] = true,
                    [NotificationType.SessionComplete] = true,
                    [NotificationType.SystemAlert] = true
                }
            },
            Performance = new PerformanceConfiguration
            {
                MaxMemoryUsage = 1024,
                MaxCpuUsage = 80,
                EnableOptimizations = true,
                MonitoringInterval = TimeSpan.FromSeconds(5),
                DataRetentionDays = 30,
                EnableGarbageCollection = true,
                MaxQueueSize = 10000
            },
            UI = new UIConfiguration
            {
                Theme = "Dark",
                UpdateInterval = TimeSpan.FromMilliseconds(500),
                ShowPerformanceMetrics = true,
                MapVisualizationEnabled = true,
                AutoSaveLayout = true,
                CustomSettings = new Dictionary<string, object>()
            },
            Database = new DatabaseConfiguration
            {
                ConnectionString = "",
                EnableAutoBackup = true,
                BackupInterval = TimeSpan.FromDays(1),
                BackupPath = "",
                MaxBackupFiles = 10
            },
            Plugins = new PluginConfiguration
            {
                EnablePlugins = true,
                DisabledPlugins = new string[0],
                PluginSettings = new Dictionary<string, Dictionary<string, object>>(),
                AllowUnsignedPlugins = false
            }
        };
    }

    public class NetworkConfiguration
    {
        public bool AutoDetectInterface { get; set; }
        public string NetworkInterface { get; set; }
        public int[] GamePorts { get; set; }
        public int PacketBufferSize { get; set; }
        public int MaxConcurrentPackets { get; set; }
        public bool FilteringEnabled { get; set; }
        public string ProtocolVersion { get; set; }
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    public class DetectionConfiguration
    {
        public EntityType[] EnabledEntityTypes { get; set; }
        public int MinimumTier { get; set; }
        public bool IgnoreCommonMobs { get; set; }
        public double DetectionRadius { get; set; }
        public TimeSpan CacheTimeout { get; set; }
        public bool EnablePredictiveDetection { get; set; }
        public string[] BlacklistedEntities { get; set; } = new string[0];
        public Dictionary<EntityType, ScanPriority> TypePriorities { get; set; } = new();
    }

    public class AvalonianConfiguration
    {
        public ScanPriority PriorityThreshold { get; set; }
        public bool ShowEstimatedValues { get; set; }
        public bool GenerateOptimalPaths { get; set; }
        public double RoomSizeEstimate { get; set; }
        public bool EnableRoomMapping { get; set; }
        public string PathOptimizationAlgorithm { get; set; }
        public bool EnableThreatAssessment { get; set; } = true;
        public bool ShowRecommendedStrategies { get; set; } = true;
    }

    public class NotificationConfiguration
    {
        public bool EnableDesktopNotifications { get; set; }
        public bool EnableSoundAlerts { get; set; }
        public NotificationPriority MinimumPriority { get; set; }
        public DiscordNotificationConfig Discord { get; set; }
        public EmailNotificationConfig Email { get; set; } = new();
        public Dictionary<NotificationType, bool> TypeSettings { get; set; } = new();
    }

    public class DiscordNotificationConfig
    {
        public bool Enabled { get; set; }
        public string WebhookUrl { get; set; }
        public NotificationPriority MinPriority { get; set; }
        public string Username { get; set; } = "Albion Scanner";
        public string AvatarUrl { get; set; }
    }

    public class EmailNotificationConfig
    {
        public bool Enabled { get; set; }
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; } = 587;
        public string Username { get; set; }
        public string Password { get; set; }
        public string FromAddress { get; set; }
        public string ToAddress { get; set; }
        public bool UseSSL { get; set; } = true;
    }

    public class PerformanceConfiguration
    {
        public long MaxMemoryUsage { get; set; }
        public double MaxCpuUsage { get; set; }
        public bool EnableOptimizations { get; set; }
        public TimeSpan MonitoringInterval { get; set; }
        public int DataRetentionDays { get; set; }
        public bool EnableGarbageCollection { get; set; } = true;
        public int MaxQueueSize { get; set; } = 10000;
    }

    public class UIConfiguration
    {
        public string Theme { get; set; }
        public TimeSpan UpdateInterval { get; set; }
        public bool ShowPerformanceMetrics { get; set; }
        public bool MapVisualizationEnabled { get; set; }
        public bool AutoSaveLayout { get; set; }
        public Dictionary<string, object> CustomSettings { get; set; } = new();
    }

    public class DatabaseConfiguration
    {
        public string ConnectionString { get; set; }
        public bool EnableAutoBackup { get; set; } = true;
        public TimeSpan BackupInterval { get; set; } = TimeSpan.FromDays(1);
        public string BackupPath { get; set; }
        public int MaxBackupFiles { get; set; } = 10;
    }

    public class PluginConfiguration
    {
        public bool EnablePlugins { get; set; } = true;
        public string[] DisabledPlugins { get; set; } = new string[0];
        public Dictionary<string, Dictionary<string, object>> PluginSettings { get; set; } = new();
        public bool AllowUnsignedPlugins { get; set; } = false;
    }
}