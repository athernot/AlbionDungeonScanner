using System;
using System.Collections.Generic;
using System.Linq;
using AlbionDungeonScanner.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Avalonian
{
    public class AvalonianDetector
    {
        private readonly Dictionary<string, AvalonianEntityData> _avalonianEntities;
        private readonly List<AvalonianRoom> _detectedRooms;
        private readonly ILogger<AvalonianDetector> _logger;

        public AvalonianDetector(ILogger<AvalonianDetector> logger = null)
        {
            _logger = logger;
            _avalonianEntities = LoadAvalonianData();
            _detectedRooms = new List<AvalonianRoom>();
        }

        private Dictionary<string, AvalonianEntityData> LoadAvalonianData()
        {
            // Data khusus untuk Avalonian dungeons berdasarkan ao-bin-dumps
            return new Dictionary<string, AvalonianEntityData>
            {
                // Avalonian Chests
                ["T4_TREASURE_DECORATIVE_CHEST_A"] = new AvalonianEntityData
                {
                    Name = "Avalonian Chest (T4)",
                    Type = AvalonianEntityType.Chest,
                    Tier = 4,
                    Value = EstimatedValue.Medium,
                    Priority = ScanPriority.High
                },
                ["T5_TREASURE_DECORATIVE_CHEST_A"] = new AvalonianEntityData
                {
                    Name = "Avalonian Chest (T5)",
                    Type = AvalonianEntityType.Chest,
                    Tier = 5,
                    Value = EstimatedValue.High,
                    Priority = ScanPriority.High
                },
                ["T6_TREASURE_DECORATIVE_CHEST_A"] = new AvalonianEntityData
                {
                    Name = "Avalonian Chest (T6)",
                    Type = AvalonianEntityType.Chest,
                    Tier = 6,
                    Value = EstimatedValue.VeryHigh,
                    Priority = ScanPriority.Critical
                },
                ["T7_TREASURE_DECORATIVE_CHEST_A"] = new AvalonianEntityData
                {
                    Name = "Avalonian Chest (T7)",
                    Type = AvalonianEntityType.Chest,
                    Tier = 7,
                    Value = EstimatedValue.VeryHigh,
                    Priority = ScanPriority.Critical
                },
                ["T8_TREASURE_DECORATIVE_CHEST_A"] = new AvalonianEntityData
                {
                    Name = "Avalonian Chest (T8)",
                    Type = AvalonianEntityType.Chest,
                    Tier = 8,
                    Value = EstimatedValue.Legendary,
                    Priority = ScanPriority.Critical
                },
                
                // Avalonian Bosses
                ["MOB_AVALON_UNDEAD_BOSS_KEEPER"] = new AvalonianEntityData
                {
                    Name = "Avalonian Keeper",
                    Type = AvalonianEntityType.Boss,
                    Tier = 6,
                    Value = EstimatedValue.VeryHigh,
                    Priority = ScanPriority.Critical,
                    Abilities = new[] { "Teleport", "AoE Damage", "Summon Minions" }
                },
                ["MOB_AVALON_CONSTRUCT_BOSS_SENTINEL"] = new AvalonianEntityData
                {
                    Name = "Avalonian Sentinel",
                    Type = AvalonianEntityType.Boss,
                    Tier = 7,
                    Value = EstimatedValue.Legendary,
                    Priority = ScanPriority.Critical,
                    Abilities = new[] { "Laser Beam", "Shield", "Charge Attack" }
                },
                ["MOB_AVALON_DEMON_BOSS_VOIDLORD"] = new AvalonianEntityData
                {
                    Name = "Avalonian Voidlord",
                    Type = AvalonianEntityType.Boss,
                    Tier = 8,
                    Value = EstimatedValue.Legendary,
                    Priority = ScanPriority.Critical,
                    Abilities = new[] { "Void Blast", "Portal Summon", "Life Drain" }
                },
                
                // Avalonian Elite Mobs
                ["MOB_AVALON_UNDEAD_ELITE_MAGE"] = new AvalonianEntityData
                {
                    Name = "Avalonian Elite Mage",
                    Type = AvalonianEntityType.EliteMob,
                    Tier = 5,
                    Value = EstimatedValue.High,
                    Priority = ScanPriority.High
                },
                ["MOB_AVALON_CONSTRUCT_ELITE_GOLEM"] = new AvalonianEntityData
                {
                    Name = "Avalonian Elite Golem",
                    Type = AvalonianEntityType.EliteMob,
                    Tier = 6,
                    Value = EstimatedValue.High,
                    Priority = ScanPriority.High
                },
                ["MOB_AVALON_DEMON_ELITE_WARLOCK"] = new AvalonianEntityData
                {
                    Name = "Avalonian Elite Warlock",
                    Type = AvalonianEntityType.EliteMob,
                    Tier = 7,
                    Value = EstimatedValue.VeryHigh,
                    Priority = ScanPriority.High
                },
                
                // Avalonian Resources
                ["TREASURE_AVALON_TOME_T4"] = new AvalonianEntityData
                {
                    Name = "Avalonian Tome (T4)",
                    Type = AvalonianEntityType.Resource,
                    Tier = 4,
                    Value = EstimatedValue.Medium,
                    Priority = ScanPriority.Medium
                },
                ["TREASURE_AVALON_TOME_T5"] = new AvalonianEntityData
                {
                    Name = "Avalonian Tome (T5)",
                    Type = AvalonianEntityType.Resource,
                    Tier = 5,
                    Value = EstimatedValue.High,
                    Priority = ScanPriority.Medium
                },
                ["TREASURE_AVALON_TOME_T6"] = new AvalonianEntityData
                {
                    Name = "Avalonian Tome (T6)",
                    Type = AvalonianEntityType.Resource,
                    Tier = 6,
                    Value = EstimatedValue.VeryHigh,
                    Priority = ScanPriority.High
                },
                ["TREASURE_AVALON_ARTIFACT_T6"] = new AvalonianEntityData
                {
                    Name = "Avalonian Artifact (T6)",
                    Type = AvalonianEntityType.Resource,
                    Tier = 6,
                    Value = EstimatedValue.VeryHigh,
                    Priority = ScanPriority.Critical
                },
                ["TREASURE_AVALON_ARTIFACT_T7"] = new AvalonianEntityData
                {
                    Name = "Avalonian Artifact (T7)",
                    Type = AvalonianEntityType.Resource,
                    Tier = 7,
                    Value = EstimatedValue.Legendary,
                    Priority = ScanPriority.Critical
                },
                ["TREASURE_AVALON_ARTIFACT_T8"] = new AvalonianEntityData
                {
                    Name = "Avalonian Artifact (T8)",
                    Type = AvalonianEntityType.Resource,
                    Tier = 8,
                    Value = EstimatedValue.Legendary,
                    Priority = ScanPriority.Critical
                }
            };
        }

        public AvalonianScanResult ProcessAvalonianEntity(string entityId, Vector3 position)
        {
            try
            {
                if (!_avalonianEntities.ContainsKey(entityId))
                {
                    // Try partial match for dynamic IDs
                    var matchingEntity = _avalonianEntities.FirstOrDefault(kvp => 
                        entityId.Contains(kvp.Key) || kvp.Key.Contains(entityId));
                    
                    if (matchingEntity.Key == null)
                        return null;
                    
                    entityId = matchingEntity.Key;
                }

                var entityData = _avalonianEntities[entityId];
                var room = DetermineRoom(position);
                
                var result = new AvalonianScanResult
                {
                    EntityData = entityData,
                    Position = position,
                    Room = room,
                    EstimatedLoot = CalculateEstimatedLoot(entityData),
                    ThreatLevel = CalculateThreatLevel(entityData),
                    RecommendedStrategy = GetRecommendedStrategy(entityData)
                };

                // Update room tracking
                if (room != null)
                {
                    room.Entities.Add(result);
                    if (!_detectedRooms.Any(r => r.GridX == room.GridX && r.GridY == room.GridY))
                    {
                        _detectedRooms.Add(room);
                    }
                }

                _logger?.LogInformation($"Processed Avalonian entity: {entityData.Name} (T{entityData.Tier}) - Value: {entityData.Value}");
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error processing Avalonian entity: {entityId}");
                return null;
            }
        }

        private AvalonianRoom DetermineRoom(Vector3 position)
        {
            try
            {
                // Logic untuk menentukan room berdasarkan koordinat
                // Avalonian dungeons memiliki layout yang dapat diprediksi
                var roomX = (int)Math.Floor(position.X / 50); // Asumsi room size 50x50
                var roomY = (int)Math.Floor(position.Z / 50);
                
                var existingRoom = _detectedRooms.FirstOrDefault(r => r.GridX == roomX && r.GridY == roomY);
                if (existingRoom != null)
                    return existingRoom;

                return new AvalonianRoom
                {
                    GridX = roomX,
                    GridY = roomY,
                    RoomType = DetermineRoomType(position),
                    Entities = new List<AvalonianScanResult>()
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error determining room");
                return null;
            }
        }

        private AvalonianRoomType DetermineRoomType(Vector3 position)
        {
            // Simplified room type detection - dapat diperluas dengan pattern recognition
            var distance = Math.Sqrt(position.X * position.X + position.Z * position.Z);
            
            if (distance > 200) return AvalonianRoomType.Boss;
            if (distance > 150) return AvalonianRoomType.Elite;
            if (distance > 100) return AvalonianRoomType.Treasure;
            
            return AvalonianRoomType.Normal;
        }

        private LootEstimate CalculateEstimatedLoot(AvalonianEntityData entity)
        {
            var baseValue = GetBaseValue(entity.Tier, entity.Value);
            
            return new LootEstimate
            {
                MinSilver = (int)(baseValue * 0.7),
                MaxSilver = (int)(baseValue * 1.3),
                PossibleItems = GetPossibleLoot(entity),
                Fame = CalculateFame(entity)
            };
        }

        private int GetBaseValue(int tier, EstimatedValue value)
        {
            var baseTierValue = tier * 1000;
            return value switch
            {
                EstimatedValue.Low => baseTierValue,
                EstimatedValue.Medium => (int)(baseTierValue * 1.5),
                EstimatedValue.High => baseTierValue * 2,
                EstimatedValue.VeryHigh => baseTierValue * 3,
                EstimatedValue.Legendary => baseTierValue * 5,
                _ => baseTierValue
            };
        }

        private string[] GetPossibleLoot(AvalonianEntityData entity)
        {
            return entity.Type switch
            {
                AvalonianEntityType.Chest => new[] { "Avalonian Tome", "Avalonian Artifact", "Silver", "Premium Items", "Rare Equipment" },
                AvalonianEntityType.Boss => new[] { "Legendary Gear", "Avalonian Shards", "Unique Artifacts", "Large Silver", "Boss Tokens" },
                AvalonianEntityType.EliteMob => new[] { "Rare Gear", "Avalonian Energy", "Silver", "Enchanted Items" },
                AvalonianEntityType.Resource => new[] { "Avalonian Materials", "Crafting Resources", "Tomes", "Artifacts" },
                _ => new[] { "Unknown" }
            };
        }

        private int CalculateFame(AvalonianEntityData entity)
        {
            return entity.Type switch
            {
                AvalonianEntityType.Boss => entity.Tier * 2000,
                AvalonianEntityType.EliteMob => entity.Tier * 1000,
                AvalonianEntityType.Chest => entity.Tier * 500,
                AvalonianEntityType.Resource => entity.Tier * 300,
                _ => entity.Tier * 100
            };
        }

        private ThreatLevel CalculateThreatLevel(AvalonianEntityData entity)
        {
            if (entity.Type == AvalonianEntityType.Boss && entity.Tier >= 8)
                return ThreatLevel.Extreme;
            if (entity.Type == AvalonianEntityType.Boss && entity.Tier >= 6)
                return ThreatLevel.High;
            if (entity.Type == AvalonianEntityType.EliteMob && entity.Tier >= 7)
                return ThreatLevel.High;
            if (entity.Type == AvalonianEntityType.EliteMob && entity.Tier >= 5)
                return ThreatLevel.Medium;
            
            return ThreatLevel.Low;
        }

        private string GetRecommendedStrategy(AvalonianEntityData entity)
        {
            return entity.Type switch
            {
                AvalonianEntityType.Boss when entity.Name.Contains("Keeper") => 
                    "Stay mobile, watch for teleport, clear minions quickly. Use ranged damage when possible.",
                AvalonianEntityType.Boss when entity.Name.Contains("Sentinel") => 
                    "Interrupt laser beam, avoid charge attacks, use hit-and-run tactics.",
                AvalonianEntityType.Boss when entity.Name.Contains("Voidlord") => 
                    "High mobility required, avoid void zones, interrupt life drain.",
                AvalonianEntityType.EliteMob when entity.Name.Contains("Mage") => 
                    "Interrupt spells, close distance quickly, use silence abilities.",
                AvalonianEntityType.EliteMob when entity.Name.Contains("Golem") => 
                    "Use mobility, avoid standing still, target weak points.",
                AvalonianEntityType.EliteMob when entity.Name.Contains("Warlock") => 
                    "Dispel debuffs, stay spread out, focus fire.",
                AvalonianEntityType.Chest => 
                    "Clear surrounding enemies first, check for traps, bring proper gathering gear.",
                AvalonianEntityType.Resource =>
                    "Secure area first, bring appropriate gathering tools, watch for ambushes.",
                _ => "Standard engagement tactics"
            };
        }

        public AvalonianDungeonMap GenerateMap()
        {
            try
            {
                var map = new AvalonianDungeonMap
                {
                    Rooms = _detectedRooms.ToList(),
                    TotalValue = _detectedRooms.SelectMany(r => r.Entities)
                                             .Sum(e => e.EstimatedLoot.MaxSilver),
                    CompletionPercentage = CalculateCompletionPercentage(),
                    RecommendedPath = GenerateOptimalPath()
                };

                _logger?.LogInformation($"Generated Avalonian map: {map.Rooms.Count} rooms, {map.TotalValue:N0} silver value");
                return map;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error generating Avalonian map");
                return new AvalonianDungeonMap { Rooms = new List<AvalonianRoom>() };
            }
        }

        private double CalculateCompletionPercentage()
        {
            // Estimate berdasarkan room yang terdeteksi vs typical Avalonian dungeon size
            var detectedRooms = _detectedRooms.Count;
            var estimatedTotalRooms = 15; // Average Avalonian dungeon size
            
            return Math.Min(100.0, (detectedRooms / (double)estimatedTotalRooms) * 100);
        }

        private List<Vector3> GenerateOptimalPath()
        {
            try
            {
                // Generate optimal path berdasarkan value dan threat level
                var highValueEntities = _detectedRooms
                    .SelectMany(r => r.Entities)
                    .Where(e => e.EntityData.Priority >= Sc