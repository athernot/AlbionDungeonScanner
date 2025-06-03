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
        private readonly DataRepository _dataRepo; // Sebaiknya di-inject
        private readonly List<DungeonEntity> _currentEntities;
        private readonly ILogger<EntityDetector> _logger;

        public event Action<DungeonEntity> EntityDetectedEvent; // Mengganti nama agar lebih jelas ini adalah event
        public event Action<DungeonEntity> EntityRemovedEvent;

        // Ubah constructor untuk menerima DataRepository via DI
        public EntityDetector(ILogger<EntityDetector> logger, DataRepository dataRepository)
        {
            _logger = logger;
            _dataRepo = dataRepository; // Gunakan instance yang di-inject
            _currentEntities = new List<DungeonEntity>();
        }

        public void ProcessEvent(PhotonEvent photonEvent)
        {
            if (photonEvent == null) return;

            try
            {
                // Dapatkan nama event untuk logging jika ada
                // string eventName = photonEvent.IsOperationResponse ? _parser.GetOperationName(photonEvent.Code) : _parser.GetEventName(photonEvent.Code);
                // _logger?.LogTrace("Processing Event/OpResponse Code: {EventCode} ({EventName})", photonEvent.Code, eventName);

                // Logika di sini akan SANGAT bergantung pada parameter apa saja yang dikirim
                // untuk setiap event code. Anda perlu melakukan riset mendalam pada paket Albion
                // atau merujuk ke proyek open source lain yang sudah berhasil mem-parsing ini.

                switch (photonEvent.Code)
                {
                    // EventCode.NewMob dari AlbionTracker adalah 53
                    case 53: // NewMob 
                        ProcessNewMob(photonEvent.Parameters);
                        break;
                    
                    // EventCode.NewTreasureChest dari AlbionTracker adalah 51
                    case 51: // NewTreasureChest
                        ProcessNewChest(photonEvent.Parameters);
                        break;
                    
                    // Ini perlu disesuaikan dengan event code yang benar untuk "chest opened"
                    // Mungkin tidak ada event spesifik, tapi entitas chest menghilang dari daftar entitas yang dikirim server
                    case 5: // Placeholder, sesuaikan dengan EventCode yang benar untuk ChestOpened atau penghapusan entitas
                        ProcessEntityRemoved(photonEvent.Parameters, "Chest");
                        break;

                    // EventCode.Death dari AlbionTracker adalah (tidak ada yang spesifik untuk mob, mungkin bagian dari UpdateHealth atau event lain)
                    // Kita asumsikan ada event code spesifik untuk mob/player death, atau mob menghilang
                    case 4: // Placeholder untuk MobDied atau penghapusan entitas
                        ProcessEntityRemoved(photonEvent.Parameters, "Mob");
                        break;

                    // EventCode.NewHarvestableObject dari AlbionTracker adalah 50
                    case 50: // NewHarvestableObject
                        ProcessNewHarvestableObject(photonEvent.Parameters);
                        break;
                    
                    // EventCode.NewLoot dari AlbionTracker adalah 57 (mungkin relevan untuk chest atau mob drop)
                    case 57: // NewLoot 
                        ProcessNewLoot(photonEvent.Parameters);
                        break;

                    // Tambahkan case lain yang relevan, misal:
                    // case EventCodes.NewPlayer: ProcessNewPlayer(photonEvent.Parameters); break;
                    // case EventCodes.Disappear: ProcessEntityDisappear(photonEvent.Parameters); break; // Jika ada event untuk entitas hilang

                    default:
                        _logger?.LogTrace("Unhandled Photon Event Code: {EventCode}", photonEvent.Code);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing PhotonEvent with Code: {EventCode}", photonEvent.Code);
            }
        }

        private void ProcessNewMob(Dictionary<byte, object> parameters)
        {
            try
            {
                // Kunci parameter (misal 0 untuk ID, 1 untuk TypeID, 8 untuk Posisi)
                // ini HARUS disesuaikan berdasarkan analisis paket Albion yang sebenarnya.
                // Saya menggunakan contoh dari beberapa parser Photon umum.
                if (!parameters.TryGetValue(0, out object entityIdObj) ||
                    !parameters.TryGetValue(1, out object typeIdObj)) // TypeId unik untuk jenis mob
                {
                    _logger?.LogWarning("NewMob event missing critical parameters (ID or TypeId).");
                    return;
                }

                string entityId = entityIdObj.ToString();
                string typeId = typeIdObj.ToString(); // Ini adalah ID yang lebih spesifik untuk tipe mob
                
                Vector3 position = ExtractPosition(parameters, 8); // Asumsi parameter 8 adalah posisi
                if (position == null)
                {
                    _logger?.LogWarning("NewMob event for ID {EntityId} missing position data.", entityId);
                    return;
                }

                var mobData = _dataRepo.GetMobData(typeId); // Gunakan typeId untuk mencari data mob
                if (mobData != null)
                {
                    var entity = new DungeonEntity
                    {
                        Id = entityId, // ID instance unik
                        Name = mobData.Name ?? typeId,
                        Type = mobData.IsBoss ? EntityType.Boss : EntityType.Mob,
                        Position = position,
                        LastSeen = DateTime.UtcNow,
                        DungeonType = DetermineDungeonType(mobData, typeId)
                    };

                    AddOrUpdateEntity(entity);
                    _logger?.LogInformation("Detected Mob: {EntityName} (ID: {EntityId}) at {Position}", entity.Name, entity.Id, entity.Position);
                }
                else
                {
                    _logger?.LogDebug("Mob data not found for TypeId: {TypeId}", typeId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing NewMob event.");
            }
        }
        
        private void ProcessNewChest(Dictionary<byte, object> parameters)
        {
            try
            {
                if (!parameters.TryGetValue(0, out object entityIdObj) ||
                    !parameters.TryGetValue(1, out object typeIdObj))
                {
                     _logger?.LogWarning("NewChest event missing critical parameters (ID or TypeId).");
                    return;
                }
                string entityId = entityIdObj.ToString();
                string typeId = typeIdObj.ToString(); // Type ID untuk jenis chest
                
                Vector3 position = ExtractPosition(parameters, 8);
                if (position == null)
                {
                    _logger?.LogWarning("NewChest event for ID {EntityId} missing position data.", entityId);
                    return;
                }

                // Chest data mungkin ada di _itemsData atau _chestsData khusus
                var chestData = _dataRepo.GetChestData(typeId) ?? _dataRepo.GetItemData(typeId); 
                if (chestData != null)
                {
                    var entity = new DungeonEntity
                    {
                        Id = entityId,
                        Name = chestData.Name ?? typeId,
                        Type = EntityType.Chest,
                        Position = position,
                        LastSeen = DateTime.UtcNow,
                        DungeonType = DetermineDungeonType(chestData, typeId)
                    };
                    AddOrUpdateEntity(entity);
                    _logger?.LogInformation("Detected Chest: {EntityName} (ID: {EntityId}) at {Position}", entity.Name, entity.Id, entity.Position);
                }
                 else
                {
                    _logger?.LogDebug("Chest data not found for TypeId: {TypeId}", typeId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing NewChest event.");
            }
        }
        
        private void ProcessNewHarvestableObject(Dictionary<byte, object> parameters)
        {
            try
            {
                if (!parameters.TryGetValue(0, out object entityIdObj) || 
                    !parameters.TryGetValue(1, out object typeIdObj) ||
                    !parameters.TryGetValue(7, out object tierObj)) // Asumsi parameter 7 adalah tier
                {
                     _logger?.LogWarning("NewHarvestableObject event missing critical parameters.");
                    return;
                }

                string entityId = entityIdObj.ToString();
                string typeId = typeIdObj.ToString();
                byte tier = Convert.ToByte(tierObj);

                Vector3 position = ExtractPosition(parameters, 8);
                 if (position == null)
                {
                    _logger?.LogWarning("NewHarvestableObject event for ID {EntityId} missing position data.", entityId);
                    return;
                }

                // Menggunakan typeId untuk mencari nama dari DataRepository
                var itemData = _dataRepo.GetItemData(typeId);
                string resourceName = itemData?.Name ?? typeId;

                if (IsValuableResource(typeId, tier))
                {
                    var entity = new DungeonEntity
                    {
                        Id = entityId,
                        Name = $"{resourceName} (T{tier})",
                        Type = EntityType.ResourceNode,
                        Position = position,
                        LastSeen = DateTime.UtcNow,
                        DungeonType = DetermineDungeonType(itemData, typeId) 
                    };
                    AddOrUpdateEntity(entity);
                     _logger?.LogInformation("Detected Resource: {EntityName} (ID: {EntityId}) at {Position}", entity.Name, entity.Id, entity.Position);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing NewHarvestableObject event.");
            }
        }

        private void ProcessNewLoot(Dictionary<byte, object> parameters)
        {
            // Parameter untuk loot bisa berisi:
            // - ID kontainer (misalnya, mayat mob atau chest yang baru dibuka)
            // - Daftar item (array ID item dan jumlahnya)
            // Anda perlu mengurai ini dan mungkin menghubungkannya dengan entitas yang ada.
             _logger?.LogDebug("Processing NewLoot event. Parameters: {ParameterCount}", parameters.Count);
            // Implementasi detail bergantung pada struktur parameter loot
        }


        private Vector3 ExtractPosition(Dictionary<byte, object> parameters, byte positionParameterKey = 8)
        {
            // Posisi seringkali datang sebagai array float[] {x, y, z} atau float[] {x, z}
            // Kunci parameter untuk posisi bisa bervariasi, umumnya antara 3-10.
            // Referensi: AlbionTracker/Albion.Common/Photon/ParameterCode.cs (ParameterCode.Position = 8)
            if (parameters.TryGetValue(positionParameterKey, out object posObj))
            {
                if (posObj is float[] posArray)
                {
                    if (posArray.Length == 2) // Seringkali hanya X, Z (Y adalah ketinggian/ground)
                    {
                        return new Vector3 { X = posArray[0], Y = 0, Z = posArray[1] };
                    }
                    else if (posArray.Length == 3)
                    {
                        return new Vector3 { X = posArray[0], Y = posArray[1], Z = posArray[2] };
                    }
                }
                else if (posObj is object[] objArray && objArray.Length >= 2 && objArray.All(o => o is float))
                {
                    // Kadang-kadang array object berisi float
                     var floatArray = objArray.Cast<float>().ToArray();
                     if (floatArray.Length == 2) return new Vector3 { X = floatArray[0], Y = 0, Z = floatArray[1] };
                     if (floatArray.Length == 3) return new Vector3 { X = floatArray[0], Y = floatArray[1], Z = floatArray[2] };
                }
                _logger?.LogWarning("Position data found but in unexpected format: {PosObjType}", posObj.GetType().Name);
            }
            return null; // Atau throw jika posisi wajib ada
        }

        private DungeonType DetermineDungeonType(dynamic entityData, string entityOrTypeId)
        {
            string nameToCheck = entityData?.Name?.ToString() ?? entityOrTypeId ?? "";
            nameToCheck = nameToCheck.ToUpperInvariant();

            if (nameToCheck.Contains("AVALON") || nameToCheck.Contains("AVA_")) // Mencakup AVA_MOB, AVA_CHEST, dll.
                return DungeonType.Avalonian;
            if (nameToCheck.Contains("CORRUPTED") || nameToCheck.Contains("HELLGATE")) // Hellgate seringkali bagian dari Corrupted
                return DungeonType.Corrupted;
            if (nameToCheck.Contains("GROUP") || nameToCheck.Contains("ELITE")) // Elite mob biasanya di group dungeon
                return DungeonType.Group;
            if (nameToCheck.Contains("SOLO") || nameToCheck.Contains("STANDARD"))
                return DungeonType.Solo;
            
            // Default berdasarkan konteks umum jika tidak spesifik
            // Ini bisa lebih kompleks, misal dengan mengetahui cluster map saat ini
            return DungeonType.Randomized; 
        }

        private void ProcessEntityRemoved(Dictionary<byte, object> parameters, string entityCategoryHint)
        {
            try
            {
                // ID entitas yang dihapus biasanya ada di parameter pertama (key 0)
                if (!parameters.TryGetValue(0, out object entityIdObj))
                {
                    _logger?.LogWarning("EntityRemoved event missing entity ID parameter.");
                    return;
                }
                string entityId = entityIdObj.ToString();
                
                var entity = _currentEntities.FirstOrDefault(e => e.Id == entityId);
                if (entity != null)
                {
                    _currentEntities.Remove(entity);
                    EntityRemovedEvent?.Invoke(entity);
                    _logger?.LogInformation("Entity Removed ({Hint}): {EntityName} (ID: {EntityId})", entityCategoryHint, entity.Name, entity.Id);
                }
                else
                {
                    _logger?.LogDebug("Attempted to remove entity ID {EntityId} ({Hint}), but it was not found in current entities.", entityId, entityCategoryHint);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing EntityRemoved event for {Hint}", entityCategoryHint);
            }
        }
        
        private bool IsValuableResource(string typeId, byte tier)
        {
            // Tier tinggi selalu berharga
            if (tier >= 6) return true;
            
            // Resource Avalonian
            if (typeId.ToUpperInvariant().Contains("AVALON")) return true;

            // Contoh resource spesifik lain yang dianggap berharga
            // if (typeId.Contains("T5_GEM_UNCUT")) return true;

            return false; 
        }

        private void AddOrUpdateEntity(DungeonEntity entity)
        {
            var existingEntity = _currentEntities.FirstOrDefault(e => e.Id == entity.Id);
            if (existingEntity != null)
            {
                // Update existing entity (misalnya posisi, LastSeen)
                existingEntity.Position = entity.Position;
                existingEntity.LastSeen = DateTime.UtcNow;
                // Tidak memicu EntityDetectedEvent lagi jika hanya update
            }
            else
            {
                _currentEntities.Add(entity);
                EntityDetectedEvent?.Invoke(entity);
            }
        }


        public List<DungeonEntity> GetCurrentEntities() => new List<DungeonEntity>(_currentEntities); // Kembalikan copy

        public void ClearEntities()
        {
            _currentEntities.Clear();
            _logger?.LogInformation("All current entities cleared.");
        }
    }
}