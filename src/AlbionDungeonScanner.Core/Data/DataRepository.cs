using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Data
{
    public class DataRepository
    {
        private Dictionary<string, dynamic> _mobsData;
        private Dictionary<string, dynamic> _chestsData;
        private Dictionary<string, dynamic> _itemsData;
        private readonly HttpClient _httpClient;
        private readonly ILogger<DataRepository> _logger;
        private DateTime _lastDataLoad = DateTime.MinValue;

        public DataRepository(ILogger<DataRepository> logger = null)
        {
            _httpClient = new HttpClient();
            _logger = logger;
            _mobsData = new Dictionary<string, dynamic>();
            _chestsData = new Dictionary<string, dynamic>();
            _itemsData = new Dictionary<string, dynamic>();
            
            _ = Task.Run(LoadDataAsync);
        }

        private async Task LoadDataAsync()
        {
            try
            {
                _logger?.LogInformation("Loading game data from ao-bin-dumps...");

                // Load data dari ao-bin-dumps JSON files
                var mobsTask = LoadJsonFromUrlAsync("https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/mobs.json");
                var itemsTask = LoadJsonFromUrlAsync("https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/items.json");

                await Task.WhenAll(mobsTask, itemsTask);

                var mobsJson = await mobsTask;
                var itemsJson = await itemsTask;

                if (!string.IsNullOrEmpty(mobsJson))
                {
                    _mobsData = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(mobsJson) 
                               ?? new Dictionary<string, dynamic>();
                }

                if (!string.IsNullOrEmpty(itemsJson))
                {
                    _itemsData = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(itemsJson) 
                                ?? new Dictionary<string, dynamic>();
                    
                    // Process chest data dari items.json
                    _chestsData = _itemsData.Where(kvp => 
                        kvp.Value.ToString().Contains("chest", StringComparison.OrdinalIgnoreCase) ||
                        kvp.Value.ToString().Contains("treasure", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }

                _lastDataLoad = DateTime.UtcNow;
                _logger?.LogInformation($"Loaded {_mobsData.Count} mobs, {_chestsData.Count} chests, {_itemsData.Count} items");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading game data");
                LoadFallbackData();
            }
        }

        private async Task<string> LoadJsonFromUrlAsync(string url)
        {
            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                return await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, $"Failed to load data from {url}");
                return null;
            }
        }

        private void LoadFallbackData()
        {
            _logger?.LogInformation("Loading fallback data...");
            
            // Fallback data untuk testing ketika API tidak tersedia
            _mobsData = new Dictionary<string, dynamic>
            {
                ["MOB_AVALON_UNDEAD_BOSS_KEEPER"] = new { Name = "Avalonian Keeper", IsBoss = true, Tier = 6 },
                ["MOB_AVALON_CONSTRUCT_BOSS_SENTINEL"] = new { Name = "Avalonian Sentinel", IsBoss = true, Tier = 7 },
                ["MOB_AVALON_UNDEAD_ELITE_MAGE"] = new { Name = "Avalonian Elite Mage", IsBoss = false, Tier = 5 },
                ["MOB_AVALON_CONSTRUCT_ELITE_GOLEM"] = new { Name = "Avalonian Elite Golem", IsBoss = false, Tier = 6 },
                ["MOB_AVALON_UNDEAD_STANDARD"] = new { Name = "Avalonian Undead", IsBoss = false, Tier = 4 },
                ["MOB_CORRUPTED_DEMON"] = new { Name = "Corrupted Demon", IsBoss = false, Tier = 5 },
                ["MOB_DUNGEON_BOSS_GENERIC"] = new { Name = "Dungeon Boss", IsBoss = true, Tier = 5 }
            };

            _chestsData = new Dictionary<string, dynamic>
            {
                ["T4_TREASURE_DECORATIVE_CHEST_A"] = new { Name = "Avalonian Chest (T4)", Tier = 4 },
                ["T5_TREASURE_DECORATIVE_CHEST_A"] = new { Name = "Avalonian Chest (T5)", Tier = 5 },
                ["T6_TREASURE_DECORATIVE_CHEST_A"] = new { Name = "Avalonian Chest (T6)", Tier = 6 },
                ["T7_TREASURE_DECORATIVE_CHEST_A"] = new { Name = "Avalonian Chest (T7)", Tier = 7 },
                ["T8_TREASURE_DECORATIVE_CHEST_A"] = new { Name = "Avalonian Chest (T8)", Tier = 8 },
                ["TREASURE_CHEST_CORRUPTED"] = new { Name = "Corrupted Chest", Tier = 5 },
                ["TREASURE_CHEST_DUNGEON_SOLO"] = new { Name = "Solo Dungeon Chest", Tier = 4 },
                ["TREASURE_CHEST_DUNGEON_GROUP"] = new { Name = "Group Dungeon Chest", Tier = 6 }
            };

            _itemsData = new Dictionary<string, dynamic>
            {
                ["TREASURE_AVALON_TOME_T4"] = new { Name = "Avalonian Tome (T4)", Tier = 4 },
                ["TREASURE_AVALON_TOME_T5"] = new { Name = "Avalonian Tome (T5)", Tier = 5 },
                ["TREASURE_AVALON_TOME_T6"] = new { Name = "Avalonian Tome (T6)", Tier = 6 },
                ["TREASURE_AVALON_ARTIFACT_T6"] = new { Name = "Avalonian Artifact (T6)", Tier = 6 },
                ["TREASURE_AVALON_ARTIFACT_T7"] = new { Name = "Avalonian Artifact (T7)", Tier = 7 },
                ["TREASURE_AVALON_ARTIFACT_T8"] = new { Name = "Avalonian Artifact (T8)", Tier = 8 }
            };
        }

        public virtual dynamic GetMobData(string mobId)
        {
            if (string.IsNullOrEmpty(mobId))
                return null;

            // Try exact match first
            if (_mobsData.ContainsKey(mobId))
                return _mobsData[mobId];

            // Try partial match
            var partialMatch = _mobsData.FirstOrDefault(kvp => 
                kvp.Key.Contains(mobId, StringComparison.OrdinalIgnoreCase) ||
                mobId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));

            return partialMatch.Value;
        }

        public virtual dynamic GetChestData(string chestId)
        {
            if (string.IsNullOrEmpty(chestId))
                return null;

            // Try exact match first
            if (_chestsData.ContainsKey(chestId))
                return _chestsData[chestId];

            // Try partial match
            var partialMatch = _chestsData.FirstOrDefault(kvp => 
                kvp.Key.Contains(chestId, StringComparison.OrdinalIgnoreCase) ||
                chestId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));

            return partialMatch.Value;
        }

        public virtual dynamic GetItemData(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            // Try exact match first
            if (_itemsData.ContainsKey(itemId))
                return _itemsData[itemId];

            // Try partial match
            var partialMatch = _itemsData.FirstOrDefault(kvp => 
                kvp.Key.Contains(itemId, StringComparison.OrdinalIgnoreCase) ||
                itemId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase));

            return partialMatch.Value;
        }

        public async Task RefreshDataAsync()
        {
            await LoadDataAsync();
        }

        public bool IsDataLoaded()
        {
            return _mobsData.Any() && _chestsData.Any() && _itemsData.Any();
        }

        public DateTime GetLastDataLoadTime()
        {
            return _lastDataLoad;
        }

        public Dictionary<string, int> GetDataStatistics()
        {
            return new Dictionary<string, int>
            {
                ["Mobs"] = _mobsData.Count,
                ["Chests"] = _chestsData.Count,
                ["Items"] = _itemsData.Count
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}