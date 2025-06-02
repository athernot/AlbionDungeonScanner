using System;
using System.Collections.Generic;
using System.Linq;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Data;
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Detection
{
    public class EntityDetector
    {
        private readonly DataRepository _dataRepo;
        private readonly List<DungeonEntity> _currentEntities;
        private readonly ILogger<EntityDetector> _logger;

        public event Action<DungeonEntity> OnEntityDetected;
        public event Action<DungeonEntity> OnEntityRemoved;

        public EntityDetector(ILogger<EntityDetector> logger = null)
        {
            _dataRepo = new DataRepository();
            _currentEntities = new List<DungeonEntity>();
            _logger = logger;
        }

        public void ProcessEvent(PhotonEvent photonEvent)
        {
            try
            {
                switch (photonEvent.Code)
                {
                    case 1: // NewMob
                        ProcessNewMob(photonEvent.Parameters);
                        break;
                    case 3: // NewChest
                        ProcessNewChest(photonEvent.Parameters);
                        break;
                    case 5: // ChestOpened
                        ProcessChestOpened(photonEvent.Parameters);
                        break;
                    case 4: // MobDied
                        ProcessMobDied(photonEvent.Parameters);
                        break;
                    case 6: // NewHarvestableObject
                        ProcessNewHarvestableObject(photonEvent.Parameters);
                        break;
                    case 8: // NewTreasure
                        ProcessNewTreasure(photonEvent.Parameters);
                        break;
                    default:
                        // Unknown event code
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error processing event with code {photonEvent.Code}");
            }
        }

        private void ProcessNewMob(Dictionary<byte, object> parameters)
        {
            try
            {
                var mobId = parameters.ContainsKey(1) ? parameters[1].ToString() : "";
                var position = ExtractPosition(parameters);
                
                if (string.IsNullOrEmpty(mobId))
                    return;

                var mobData = _dataRepo.GetMobData(mobId);
                if (mobData != null)
                {
                    var entity = new DungeonEntity
                    {
                        Id = mobId,
                        Name = mobData.Name ?? mobId,
                        Type = mobData.IsBoss ? EntityType.Boss : EntityType.Mob,
                        Position = position,
                        LastSeen = DateTime.Now,
                        DungeonType = DetermineDungeonType(mobData, mobId)
                    };

                    _currentEntities.Add(entity);
                    OnEntityDetected?.Invoke(entity);
                    
                    _logger?.LogDebug($"Detected mob: {entity.Name} at {entity.Position}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing new mob");
            }
        }

        private void ProcessNewChest(Dictionary<byte, object> parameters)
        {
            try
            {
                var chestId = parameters.ContainsKey(1) ? parameters[1].ToString() : "";
                var position = ExtractPosition(parameters);
                
                if (string.IsNullOrEmpty(chestId))
                    return;

                var chestData = _dataRepo.GetChestData(chestId);
                if (chestData != null)
                {
                    var entity = new DungeonEntity
                    {
                        Id = chestId,
                        Name = chestData.Name ?? chestId,
                        Type = EntityType.Chest,
                        Position = position,
                        LastSeen = DateTime.Now,
                        DungeonType = DetermineDungeonType(chestData, chestId)
                    };

                    _currentEntities.Add(entity);
                    OnEntityDetected?.Invoke(entity);
                    
                    _logger?.LogDebug($"Detected chest: {entity.Name} at {entity.Position}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing new chest");
            }
        }

        private void ProcessNewHarvestableObject(Dictionary<byte, object> parameters)
        {
            try
            {
                var resourceId = parameters.ContainsKey(1) ? parameters[1].ToString() : "";
                var position = ExtractPosition(parameters);
                
                if (string.IsNullOrEmpty(resourceId))
                    return;

                var resourceData = _dataRepo.GetItemData(resourceId);
                if (resourceData != null && IsValuableResource(resourceId))
                {
                    var entity = new DungeonEntity
                    {
                        Id = resourceId,
                        Name = resourceData.Name ?? resourceId,
                        Type = EntityType.ResourceNode,
                        Position = position,
                        LastSeen = DateTime.Now,
                        DungeonType = DetermineDungeonType(resourceData, resourceId)
                    };

                    _currentEntities.Add(entity);
                    OnEntityDetected?.Invoke(entity);
                    
                    _logger?.LogDebug($"Detected resource: {entity.Name} at {entity.Position}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing new harvestable object");
            }
        }

        private void ProcessNewTreasure(Dictionary<byte, object> parameters)
        {
            try
            {
                var treasureId = parameters.ContainsKey(1) ? parameters[1].ToString() : "";
                var position = ExtractPosition(parameters);
                
                if (string.IsNullOrEmpty(treasureId))
                    return;

                var entity = new DungeonEntity
                {
                    Id = treasureId,
                    Name = GetTreasureName(treasureId),
                    Type = EntityType.Chest,
                    Position = position,
                    LastSeen = DateTime.Now,
                    DungeonType = DetermineDungeonType(null, treasureId)
                };

                _currentEntities.Add(entity);
                OnEntityDetected?.Invoke(entity);
                
                _logger?.LogDebug($"Detected treasure: {entity.Name} at {entity.Position}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing new treasure");
            }
        }

        private Vector3 ExtractPosition(Dictionary<byte, object> parameters)
        {
            try
            {
                var x = parameters.ContainsKey(2) ? Convert.ToSingle(parameters[2]) : 0f;
                var y = parameters.ContainsKey(3) ? Convert.ToSingle(parameters[3]) : 0f;
                var z = parameters.ContainsKey(4) ? Convert.ToSingle(parameters[4]) : 0f;
                
                return new Vector3 { X = x, Y = y, Z = z };
            }
            catch
            {
                return new Vector3 { X = 0, Y = 0, Z = 0 };
            }
        }

        private DungeonType DetermineDungeonType(dynamic entityData, string entityId)
        {
            try
            {
                if (entityId.Contains("AVALON") || entityId.Contains("Avalonian"))
                    return DungeonType.Avalonian;
                
                if (entityData?.Name != null)
                {
                    var name = entityData.Name.ToString();
                    if (name.Contains("Avalonian"))
                        return DungeonType.Avalonian;
                    if (name.Contains("Corrupted"))
                        return DungeonType.Corrupted;
                    if (name.Contains("Group"))
                        return DungeonType.Group;
                }
                
                return DungeonType.Solo; // Default
            }
            catch
            {
                return DungeonType.Solo;
            }
        }

        private void ProcessChestOpened(Dictionary<byte, object> parameters)
        {
            try
            {
                var chestId = parameters.ContainsKey(1) ? parameters[1].ToString() : "";
                var entity = _currentEntities.FirstOrDefault(e => e.Id == chestId);
                if (entity != null)
                {
                    _currentEntities.Remove(entity);
                    OnEntityRemoved?.Invoke(entity);
                    
                    _logger?.LogDebug($"Chest opened and removed: {entity.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing chest opened");
            }
        }

        private void ProcessMobDied(Dictionary<byte, object> parameters)
        {
            try
            {
                var mobId = parameters.ContainsKey(1) ? parameters[1].ToString() : "";
                var entity = _currentEntities.FirstOrDefault(e => e.Id == mobId);
                if (entity != null)
                {
                    _currentEntities.Remove(entity);
                    OnEntityRemoved?.Invoke(entity);
                    
                    _logger?.LogDebug($"Mob died and removed: {entity.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing mob died");
            }
        }

        private bool IsValuableResource(string resourceId)
        {
            // Filter hanya resource yang valuable
            var valuableResources = new[]
            {
                "TREASURE_AVALON",
                "RUNE_AVALON",
                "SOUL_AVALON",
                "RELIC_AVALON",
                "T6_", "T7_", "T8_" // High tier resources
            };

            return valuableResources.Any(valuable => resourceId.Contains(valuable));
        }

        private string GetTreasureName(string treasureId)
        {
            if (treasureId.Contains("AVALON"))
                return "Avalonian Treasure";
            if (treasureId.Contains("T6"))
                return "Tier 6 Treasure";
            if (treasureId.Contains("T7"))
                return "Tier 7 Treasure";
            if (treasureId.Contains("T8"))
                return "Tier 8 Treasure";
                
            return treasureId;
        }

        public List<DungeonEntity> GetCurrentEntities() => _currentEntities.ToList();

        public void ClearEntities()
        {
            _currentEntities.Clear();
        }

        public int GetEntityCount(EntityType? entityType = null)
        {
            if (entityType.HasValue)
                return _currentEntities.Count(e => e.Type == entityType.Value);
            
            return _currentEntities.Count;
        }
    }
}