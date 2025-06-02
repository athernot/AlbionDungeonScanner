using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AlbionDungeonScanner.Core.Models
{
    // Basic Entity Models
    public class DungeonEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public EntityType Type { get; set; }
        public Vector3 Position { get; set; }
        public DateTime LastSeen { get; set; }
        public DungeonType DungeonType { get; set; }
    }

    public enum EntityType
    {
        Chest,
        Mob,
        Boss,
        ResourceNode,
        Portal
    }

    public enum DungeonType
    {
        Solo,
        Group,
        Avalonian,
        Corrupted,
        Randomized
    }

    public class Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public override string ToString()
        {
            return $"({X:F1}, {Y:F1}, {Z:F1})";
        }
    }

    // Photon Network Models
    public class PhotonEvent
    {
        public byte Code { get; set; }
        public Dictionary<byte, object> Parameters { get; set; }
    }

    // Avalonian Models (dari artifact kedua)
    public class AvalonianEntityData
    {
        public string Name { get; set; }
        public AvalonianEntityType Type { get; set; }
        public int Tier { get; set; }
        public EstimatedValue Value { get; set; }
        public ScanPriority Priority { get; set; }
        public string[] Abilities { get; set; } = new string[0];
    }

    public class AvalonianScanResult
    {
        public AvalonianEntityData EntityData { get; set; }
        public Vector3 Position { get; set; }
        public AvalonianRoom Room { get; set; }
        public LootEstimate EstimatedLoot { get; set; }
        public ThreatLevel ThreatLevel { get; set; }
        public string RecommendedStrategy { get; set; }
    }

    public class AvalonianRoom
    {
        public int GridX { get; set; }
        public int GridY { get; set; }
        public AvalonianRoomType RoomType { get; set; }
        public List<AvalonianScanResult> Entities { get; set; } = new List<AvalonianScanResult>();
    }

    public class AvalonianDungeonMap
    {
        public List<AvalonianRoom> Rooms { get; set; }
        public int TotalValue { get; set; }
        public double CompletionPercentage { get; set; }
        public List<Vector3> RecommendedPath { get; set; }
    }

    public class LootEstimate
    {
        public int MinSilver { get; set; }
        public int MaxSilver { get; set; }
        public string[] PossibleItems { get; set; }
        public int Fame { get; set; }
    }

    // Enums
    public enum AvalonianEntityType
    {
        Chest,
        Boss,
        EliteMob,
        Resource,
        Portal,
        Trap
    }

    public enum AvalonianRoomType
    {
        Normal,
        Elite,
        Boss,
        Treasure,
        Portal,
        Shrine
    }

    public enum EstimatedValue
    {
        Low,
        Medium,
        High,
        VeryHigh,
        Legendary
    }

    public enum ScanPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum ThreatLevel
    {
        None,
        Low,
        Medium,
        High,
        Extreme
    }

    // Analytics Models
    public class ScanSession
    {
        public Guid SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public List<EntityDetection> DetectedEntities { get; set; } = new();
        public List<AvalonianScanResult> AvalonianResults { get; set; } = new();
        public SessionStatistics Statistics { get; set; }
        public SessionPerformance Performance { get; set; }
    }

    public class EntityDetection
    {
        public Guid DetectionId { get; set; }
        public string EntityId { get; set; }
        public string EntityName { get; set; }
        public EntityType EntityType { get; set; }
        public DungeonType DungeonType { get; set; }
        public Vector3 Position { get; set; }
        public DateTime DetectedAt { get; set; }
        public Guid SessionId { get; set; }
    }

    public class SessionStatistics
    {
        public int TotalEntities { get; set; }
        public int AvalonianEntities { get; set; }
        public long EstimatedTotalValue { get; set; }
        public int EstimatedFame { get; set; }
        public int UniqueEntityTypes { get; set; }
        public string MostCommonEntity { get; set; }
        public string HighestValueEntity { get; set; }
        public double AverageEntityValue { get; set; }
        public double DungeonEfficiency { get; set; }
    }

    public class SessionPerformance
    {
        public int PacketsProcessed { get; set; }
        public double PacketsPerSecond { get; set; }
        public long MemoryUsage { get; set; }
        public double CpuUsage { get; set; }
        public int ErrorCount { get; set; }
    }

    // Notification Models
    public class NotificationData
    {
        public NotificationType Type { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public NotificationPriority Priority { get; set; }
        public object Data { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> AdditionalInfo { get; set; }
    }

    public enum NotificationType
    {
        HighValueTarget,
        Boss,
        LowEfficiency,
        SessionComplete,
        Error,
        SystemAlert
    }

    public enum NotificationPriority
    {
        Low,
        Normal,
        High,
        Critical
    }

    // Market Models
    public class MarketItem
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public long SellPriceMin { get; set; }
        public long SellPriceMax { get; set; }
        public long BuyPriceMax { get; set; }
        public long BuyPriceMin { get; set; }
        public string Location { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime? SellPriceMinDate { get; set; }
        public DateTime? BuyPriceMaxDate { get; set; }
        public int SellOrderCount { get; set; }
        public int BuyOrderCount { get; set; }
        public string DataProvider { get; set; }
        public int Quality { get; set; }
        public int Tier { get; set; }
        public string Category { get; set; }
        public ItemRarity Rarity { get; set; }
        public long PreviousPrice { get; set; }
    }

    public enum ItemRarity
    {
        Normal = 0,
        Good = 1,
        Outstanding = 2,
        Excellent = 3,
        Masterpiece = 4,
        Rare = 5,
        Legendary = 6
    }

    // Performance Models
    public class PerformanceMetrics
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public long AvailableMemory { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public double PacketsPerSecond { get; set; }
        public double EntitiesDetectedPerSecond { get; set; }
    }

    // UI Models
    public class EntityViewModel : INotifyPropertyChanged
    {
        private DungeonEntity _entity;

        public EntityViewModel(DungeonEntity entity)
        {
            _entity = entity;
        }

        public string Id => _entity.Id;
        public string Name => _entity.Name;
        public string Type => _entity.Type.ToString();
        public Vector3 Position => _entity.Position;
        public DateTime LastSeen => _entity.LastSeen;
        public string DungeonType => _entity.DungeonType.ToString();

        public string PositionString => $"({Position.X:F1}, {Position.Y:F1}, {Position.Z:F1})";
        public string LastSeenString => LastSeen.ToString("HH:mm:ss");

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ScannerStatistics
    {
        public int TotalEntities { get; set; }
        public int AvalonianEntities { get; set; }
        public long EstimatedValue { get; set; }
        public DateTime ScanStartTime { get; set; } = DateTime.Now;
        public TimeSpan ScanDuration { get; set; }
        public int PacketsProcessed { get; set; }
        public double PacketsPerSecond { get; set; }
    }
}