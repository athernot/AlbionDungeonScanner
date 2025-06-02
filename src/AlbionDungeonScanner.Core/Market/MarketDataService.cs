using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using Newtonsoft.Json;
using System.Text;

namespace AlbionDungeonScanner.Core.Market
{
    public class MarketDataService
    {
        private readonly ILogger<MarketDataService> _logger;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, MarketItem> _marketCache;
        private readonly ConcurrentDictionary<string, PriceHistory> _priceHistory;
        private readonly Timer _updateTimer;
        private readonly MarketAnalyzer _analyzer;
        private readonly List<IMarketDataProvider> _dataProviders;

        public event Action<MarketUpdate> MarketDataUpdated;
        public event Action<PriceAlert> PriceAlertTriggered;

        public MarketDataService(ILogger<MarketDataService> logger = null)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _marketCache = new ConcurrentDictionary<string, MarketItem>();
            _priceHistory = new ConcurrentDictionary<string, PriceHistory>();
            _analyzer = new MarketAnalyzer(logger);
            _dataProviders = new List<IMarketDataProvider>();

            InitializeDataProviders();
            SetupUpdateTimer();
        }

        private void InitializeDataProviders()
        {
            // Albion Online Data Project API
            _dataProviders.Add(new AlbionDataProvider(_httpClient, _logger));
            
            // Additional providers can be added here
            _dataProviders.Add(new BackupMarketProvider(_logger));
        }

        private void SetupUpdateTimer()
        {
            var updateInterval = TimeSpan.FromMinutes(5); // Update every 5 minutes
            _updateTimer = new Timer(UpdateMarketData, null, TimeSpan.Zero, updateInterval);
        }

        public async Task<MarketItem> GetMarketDataAsync(string itemId)
        {
            if (_marketCache.TryGetValue(itemId, out var cachedItem) && 
                DateTime.UtcNow - cachedItem.LastUpdated < TimeSpan.FromMinutes(5))
            {
                return cachedItem;
            }

            return await FetchMarketDataAsync(itemId);
        }

        public async Task<PriceAnalysis> AnalyzeEntityValueAsync(AvalonianScanResult entity)
        {
            var marketData = await GetMarketDataAsync(entity.EntityData.Name);
            if (marketData == null)
            {
                return new PriceAnalysis
                {
                    ItemId = entity.EntityData.Name,
                    ItemName = entity.EntityData.Name,
                    EstimatedValue = entity.EstimatedLoot.MaxSilver,
                    Confidence = 0.3,
                    Recommendation = "No market data available - using estimated value",
                    Trend = TrendDirection.Unknown
                };
            }

            var analysis = await _analyzer.AnalyzePriceAsync(marketData, entity);
            return analysis;
        }

        public async Task<MarketTrends> GetMarketTrendsAsync(EntityType entityType, int tierLevel)
        {
            var relevantItems = _marketCache.Values
                .Where(item => MatchesEntityType(item, entityType) && item.Tier == tierLevel)
                .ToList();

            if (!relevantItems.Any())
            {
                return new MarketTrends
                {
                    EntityType = entityType,
                    TierLevel = tierLevel,
                    Trend = TrendDirection.Stable,
                    Confidence = 0,
                    SampleSize = 0,
                    AverageChange = 0
                };
            }

            return _analyzer.AnalyzeTrends(relevantItems, entityType, tierLevel);
        }

        public async Task<List<OpportunityAlert>> GetMarketOpportunitiesAsync()
        {
            var opportunities = new List<OpportunityAlert>();
            
            foreach (var item in _marketCache.Values)
            {
                var opportunity = await _analyzer.EvaluateOpportunityAsync(item);
                if (opportunity.Score > 0.7) // High opportunity threshold
                {
                    opportunities.Add(opportunity);
                }
            }

            return opportunities.OrderByDescending(o => o.Score).Take(10).ToList();
        }

        public async Task<ProfitProjection> CalculateDungeonProfitAsync(List<AvalonianScanResult> entities)
        {
            var totalProfit = 0L;
            var itemAnalyses = new List<ItemProfitAnalysis>();

            foreach (var entity in entities)
            {
                var marketData = await GetMarketDataAsync(entity.EntityData.Name);
                if (marketData != null)
                {
                    var itemProfit = new ItemProfitAnalysis
                    {
                        ItemName = entity.EntityData.Name,
                        Quantity = 1, // Assumed quantity
                        MarketPrice = marketData.SellPriceMin,
                        EstimatedDrop = entity.EstimatedLoot.MaxSilver,
                        Profit = marketData.SellPriceMin - GetCostBasis(entity),
                        ProfitMargin = CalculateProfitMargin(marketData, entity)
                    };

                    itemAnalyses.Add(itemProfit);
                    totalProfit += itemProfit.Profit;
                }
            }

            return new ProfitProjection
            {
                TotalEstimatedProfit = totalProfit,
                ItemAnalyses = itemAnalyses,
                ProfitPerHour = CalculateProfitPerHour(totalProfit, entities.Count),
                RiskAdjustedProfit = ApplyRiskAdjustment(totalProfit, entities),
                MarketVolatility = CalculateVolatility(itemAnalyses),
                Recommendations = GenerateProfitRecommendations(itemAnalyses)
            };
        }

        private async Task UpdateMarketData(object state)
        {
            try
            {
                _logger?.LogDebug("Updating market data...");
                
                var tasks = _dataProviders.Select(provider => provider.FetchLatestDataAsync()).ToArray();
                var results = await Task.WhenAll(tasks);
                
                var updates = new List<MarketUpdate>();
                
                foreach (var providerResults in results)
                {
                    foreach (var item in providerResults)
                    {
                        var existingItem = _marketCache.GetOrAdd(item.ItemId, item);
                        
                        if (existingItem.LastUpdated < item.LastUpdated)
                        {
                            _marketCache[item.ItemId] = item;
                            
                            // Update price history
                            UpdatePriceHistory(item);
                            
                            // Check for price alerts
                            await CheckPriceAlerts(item, existingItem);
                            
                            updates.Add(new MarketUpdate
                            {
                                ItemId = item.ItemId,
                                PreviousPrice = existingItem.SellPriceMin,
                                NewPrice = item.SellPriceMin,
                                UpdateTime = item.LastUpdated,
                                ChangePercent = CalculateChangePercent(existingItem.SellPriceMin, item.SellPriceMin)
                            });
                        }
                    }
                }

                if (updates.Any())
                {
                    _logger?.LogInformation("Updated {UpdateCount} market items", updates.Count);
                    
                    foreach (var update in updates)
                    {
                        MarketDataUpdated?.Invoke(update);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating market data");
            }
        }

        private async Task<MarketItem> FetchMarketDataAsync(string itemId)
        {
            foreach (var provider in _dataProviders)
            {
                try
                {
                    var item = await provider.GetItemDataAsync(itemId);
                    if (item != null)
                    {
                        _marketCache[itemId] = item;
                        return item;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Provider {ProviderType} failed to fetch data for {ItemId}", 
                        provider.GetType().Name, itemId);
                }
            }

            return null;
        }

        private void UpdatePriceHistory(MarketItem item)
        {
            var history = _priceHistory.GetOrAdd(item.ItemId, new PriceHistory { ItemId = item.ItemId });
            
            history.PricePoints.Add(new PricePoint
            {
                Timestamp = item.LastUpdated,
                SellPrice = item.SellPriceMin,
                BuyPrice = item.BuyPriceMax,
                Volume = item.SellOrderCount
            });

            // Keep only last 1000 points
            if (history.PricePoints.Count > 1000)
            {
                history.PricePoints = history.PricePoints.Skip(history.PricePoints.Count - 1000).ToList();
            }
        }

        private async Task CheckPriceAlerts(MarketItem newItem, MarketItem previousItem)
        {
            if (previousItem.SellPriceMin <= 0) return;

            var changePercent = CalculateChangePercent(previousItem.SellPriceMin, newItem.SellPriceMin);
            
            if (Math.Abs(changePercent) > 10) // 10% change threshold
            {
                var alert = new PriceAlert
                {
                    ItemId = newItem.ItemId,
                    ItemName = newItem.ItemName,
                    PreviousPrice = previousItem.SellPriceMin,
                    NewPrice = newItem.SellPriceMin,
                    ChangePercent = changePercent,
                    AlertType = changePercent > 0 ? AlertType.PriceIncrease : AlertType.PriceDecrease,
                    Timestamp = DateTime.UtcNow
                };

                PriceAlertTriggered?.Invoke(alert);
            }
        }

        private bool MatchesEntityType(MarketItem item, EntityType entityType)
        {
            return entityType switch
            {
                EntityType.Chest => item.Category?.Contains("treasure", StringComparison.OrdinalIgnoreCase) == true ||
                                   item.ItemName?.Contains("chest", StringComparison.OrdinalIgnoreCase) == true,
                EntityType.Boss => item.Category?.Contains("boss", StringComparison.OrdinalIgnoreCase) == true ||
                                  item.Rarity >= ItemRarity.Legendary,
                EntityType.Mob => item.Category?.Contains("mob", StringComparison.OrdinalIgnoreCase) == true,
                _ => false
            };
        }

        private long GetCostBasis(AvalonianScanResult entity)
        {
            // Calculate cost basis (repair costs, consumables, etc.)
            return entity.EntityData.Tier * 1000; // Simplified calculation
        }

        private double CalculateProfitMargin(MarketItem marketData, AvalonianScanResult entity)
        {
            var revenue = marketData.SellPriceMin;
            var cost = GetCostBasis(entity);
            
            return cost > 0 ? ((double)(revenue - cost) / cost) * 100 : 0;
        }

        private long CalculateProfitPerHour(long totalProfit, int entityCount)
        {
            // Estimate time based on entity count (simplified)
            var estimatedHours = Math.Max(1, entityCount / 10.0);
            return (long)(totalProfit / estimatedHours);
        }

        private long ApplyRiskAdjustment(long totalProfit, List<AvalonianScanResult> entities)
        {
            var avgThreatLevel = entities.Average(e => (int)e.ThreatLevel);
            var riskFactor = 1.0 - (avgThreatLevel * 0.1); // Reduce profit by threat level
            
            return (long)(totalProfit * Math.Max(0.5, riskFactor));
        }

        private double CalculateVolatility(List<ItemProfitAnalysis> analyses)
        {
            if (!analyses.Any()) return 0;
            
            var margins = analyses.Select(a => a.ProfitMargin).ToList();
            var mean = margins.Average();
            var variance = margins.Select(m => Math.Pow(m - mean, 2)).Average();
            
            return Math.Sqrt(variance);
        }

        private List<string> GenerateProfitRecommendations(List<ItemProfitAnalysis> analyses)
        {
            var recommendations = new List<string>();
            
            var highMarginItems = analyses.Where(a => a.ProfitMargin > 50).ToList();
            if (highMarginItems.Any())
            {
                recommendations.Add($"Focus on high-margin items: {string.Join(", ", highMarginItems.Take(3).Select(i => i.ItemName))}");
            }
            
            var lowMarginItems = analyses.Where(a => a.ProfitMargin < 10).ToList();
            if (lowMarginItems.Any())
            {
                recommendations.Add($"Consider avoiding low-margin items: {string.Join(", ", lowMarginItems.Take(3).Select(i => i.ItemName))}");
            }
            
            return recommendations;
        }

        private double CalculateChangePercent(long oldPrice, long newPrice)
        {
            return oldPrice > 0 ? ((double)(newPrice - oldPrice) / oldPrice) * 100 : 0;
        }

        public void Dispose()
        {
            _updateTimer?.Dispose();
            _httpClient?.Dispose();
        }
    }

    // Market Analysis Engine
    public class MarketAnalyzer
    {
        private readonly ILogger _logger;

        public MarketAnalyzer(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<PriceAnalysis> AnalyzePriceAsync(MarketItem item, AvalonianScanResult entity)
        {
            var analysis = new PriceAnalysis
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                CurrentPrice = item.SellPriceMin,
                EstimatedValue = entity.EstimatedLoot.MaxSilver,
                MarketPrice = item.SellPriceMin,
                PriceDifference = item.SellPriceMin - entity.EstimatedLoot.MaxSilver,
                Confidence = CalculatePriceConfidence(item),
                Recommendation = GeneratePriceRecommendation(item, entity),
                Trend = AnalyzePriceTrend(item),
                Support = CalculateSupportLevel(item),
                Resistance = CalculateResistanceLevel(item)
            };

            return analysis;
        }

        public MarketTrends AnalyzeTrends(List<MarketItem> items, EntityType entityType, int tierLevel)
        {
            var priceChanges = items.Where(i => i.PreviousPrice > 0)
                .Select(i => CalculateChangePercent(i.PreviousPrice, i.SellPriceMin))
                .ToList();

            if (!priceChanges.Any())
            {
                return new MarketTrends
                {
                    EntityType = entityType,
                    TierLevel = tierLevel,
                    Trend = TrendDirection.Stable,
                    Confidence = 0,
                    SampleSize = 0,
                    AverageChange = 0
                };
            }

            var avgChange = priceChanges.Average();
            var trend = DetermineTrend(avgChange);
            var confidence = CalculateTrendConfidence(priceChanges);

            return new MarketTrends
            {
                EntityType = entityType,
                TierLevel = tierLevel,
                Trend = trend,
                AverageChange = avgChange,
                Confidence = confidence,
                SampleSize = items.Count,
                StrongTrendItems = items.Where(i => Math.Abs(CalculateChangePercent(i.PreviousPrice, i.SellPriceMin)) > 15).ToList()
            };
        }

        public async Task<OpportunityAlert> EvaluateOpportunityAsync(MarketItem item)
        {
            var score = 0.0;
            var factors = new List<string>();

            // Price momentum
            if (item.PreviousPrice > 0)
            {
                var change = CalculateChangePercent(item.PreviousPrice, item.SellPriceMin);
                if (change > 10)
                {
                    score += 0.3;
                    factors.Add("Strong price increase");
                }
            }

            // Volume analysis
            if (item.SellOrderCount > 50)
            {
                score += 0.2;
                factors.Add("High trading volume");
            }

            // Profit margin
            var profitMargin = CalculateMargin(item);
            if (profitMargin > 25)
            {
                score += 0.3;
                factors.Add("High profit margin");
            }

            // Rarity factor
            if (item.Rarity >= ItemRarity.Rare)
            {
                score += 0.2;
                factors.Add("Rare item");
            }

            return new OpportunityAlert
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                OpportunityType = DetermineOpportunityType(score, factors),
                Score = score,
                Factors = factors,
                CurrentPrice = item.SellPriceMin,
                RecommendedAction = GenerateOpportunityRecommendation(score, item)
            };
        }

        // Helper methods for analysis
        private double CalculatePriceConfidence(MarketItem item)
        {
            var confidence = 0.5; // Base confidence
            
            // More recent data = higher confidence
            var ageHours = (DateTime.UtcNow - item.LastUpdated).TotalHours;
            confidence += Math.Max(0, (24 - ageHours) / 48); // Max +0.5 for very recent data
            
            // Higher volume = higher confidence
            if (item.SellOrderCount > 10) confidence += 0.2;
            if (item.SellOrderCount > 50) confidence += 0.1;
            
            return Math.Min(1.0, confidence);
        }

        private string GeneratePriceRecommendation(MarketItem item, AvalonianScanResult entity)
        {
            var marketPrice = item.SellPriceMin;
            var dropValue = entity.EstimatedLoot.MaxSilver;
            
            if (marketPrice > dropValue * 1.5)
                return "Excellent profit opportunity - market price significantly above drop value";
            
            if (marketPrice > dropValue * 1.2)
                return "Good profit potential - market price moderately above drop value";
            
            if (marketPrice < dropValue * 0.8)
                return "Below expected value - consider market timing";
                
            return "Fair market value - standard profit expected";
        }

        private TrendDirection AnalyzePriceTrend(MarketItem item)
        {
            if (item.PreviousPrice <= 0) return TrendDirection.Unknown;
            
            var change = CalculateChangePercent(item.PreviousPrice, item.SellPriceMin);
            
            return change switch
            {
                > 5 => TrendDirection.Rising,
                < -5 => TrendDirection.Falling,
                _ => TrendDirection.Stable
            };
        }

        private long CalculateSupportLevel(MarketItem item)
        {
            return (long)(item.SellPriceMin * 0.9);
        }

        private long CalculateResistanceLevel(MarketItem item)
        {
            return (long)(item.SellPriceMin * 1.1);
        }

        private TrendDirection DetermineTrend(double avgChange)
        {
            return avgChange switch
            {
                > 3 => TrendDirection.Rising,
                < -3 => TrendDirection.Falling,
                _ => TrendDirection.Stable
            };
        }

        private double CalculateTrendConfidence(List<double> changes)
        {
            if (!changes.Any()) return 0;
            
            var consistency = 1.0 - (changes.Select(c => Math.Abs(c - changes.Average())).Average() / 100.0);
            return Math.Max(0, Math.Min(1.0, consistency));
        }

        private double CalculateMargin(MarketItem item)
        {
            var estimatedCost = item.SellPriceMin * 0.7; // Assume 70% cost basis
            return ((item.SellPriceMin - estimatedCost) / estimatedCost) * 100;
        }

        private OpportunityType DetermineOpportunityType(double score, List<string> factors)
        {
            return score switch
            {
                > 0.8 => OpportunityType.Excellent,
                > 0.6 => OpportunityType.Good,
                > 0.4 => OpportunityType.Moderate,
                _ => OpportunityType.Low
            };
        }

        private string GenerateOpportunityRecommendation(double score, MarketItem item)
        {
            return score switch
            {
                > 0.8 => $"Strong buy signal for {item.ItemName} - multiple positive factors",
                > 0.6 => $"Consider targeting {item.ItemName} - good opportunity",
                > 0.4 => $"Monitor {item.ItemName} - moderate opportunity",
                _ => $"Low priority for {item.ItemName}"
            };
        }

        private double CalculateChangePercent(long oldPrice, long newPrice)
        {
            return oldPrice > 0 ? ((double)(newPrice - oldPrice) / oldPrice) * 100 : 0;
        }
    }

    // Market Data Provider Interface and Implementations
    public interface IMarketDataProvider
    {
        Task<List<MarketItem>> FetchLatestDataAsync();
        Task<MarketItem> GetItemDataAsync(string itemId);
        string ProviderName { get; }
    }

    public class AlbionDataProvider : IMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private const string BaseUrl = "https://www.albion-online-data.com/api/v2/stats/";

        public string ProviderName => "Albion Online Data Project";

        public AlbionDataProvider(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<MarketItem>> FetchLatestDataAsync()
        {
            try
            {
                // Get popular Avalonian items
                var popularItems = new[]
                {
                    "T4_TREASURE_DECORATIVE_CHEST_A",
                    "T5_TREASURE_DECORATIVE_CHEST_A",
                    "T6_TREASURE_DECORATIVE_CHEST_A",
                    "T7_TREASURE_DECORATIVE_CHEST_A",
                    "T8_TREASURE_DECORATIVE_CHEST_A"
                };

                var tasks = popularItems.Select(GetItemDataAsync).ToArray();
                var results = await Task.WhenAll(tasks);
                
                return results.Where(r => r != null).ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching data from Albion Data API");
                return new List<MarketItem>();
            }
        }

        public async Task<MarketItem> GetItemDataAsync(string itemId)
        {
            try
            {
                var url = $"{BaseUrl}prices/{itemId}?locations=Caerleon,Bridgewatch,Lymhurst,Martlock,Thetford,FortSterling";
                var response = await _httpClient.GetStringAsync(url);
                var priceData = JsonConvert.DeserializeObject<AlbionApiResponse[]>(response);

                if (priceData?.Any() == true)
                {
                    var bestPrice = priceData
                        .Where(p => p.sell_price_min > 0)
                        .OrderBy(p => p.sell_price_min)
                        .FirstOrDefault();

                    if (bestPrice != null)
                    {
                        return new MarketItem
                        {
                            ItemId = itemId,
                            ItemName = GetItemDisplayName(itemId),
                            SellPriceMin = bestPrice.sell_price_min,
                            SellPriceMax = priceData.Max(p => p.sell_price_min),
                            BuyPriceMax = priceData.Max(p => p.buy_price_max),
                            Location = bestPrice.city,
                            LastUpdated = bestPrice.sell_price_min_date,
                            SellOrderCount = priceData.Sum(p => p.sell_order_count),
                            DataProvider = ProviderName,
                            Quality = bestPrice.quality,
                            Tier = ExtractTierFromId(itemId),
                            Category = "treasure",
                            Rarity = (ItemRarity)bestPrice.quality
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching item data for {ItemId}", itemId);
                return null;
            }
        }

        private string GetItemDisplayName(string itemId)
        {
            return itemId.Replace("_", " ").Replace("T4", "Tier 4").Replace("T5", "Tier 5").Replace("T6", "Tier 6");
        }

        private int ExtractTierFromId(string itemId)
        {
            if (itemId.StartsWith("T4")) return 4;
            if (itemId.StartsWith("T5")) return 5;
            if (itemId.StartsWith("T6")) return 6;
            if (itemId.StartsWith("T7")) return 7;
            if (itemId.StartsWith("T8")) return 8;
            return 4;
        }

        private class AlbionApiResponse
        {
            public string item_id { get; set; }
            public string city { get; set; }
            public int quality { get; set; }
            public long sell_price_min { get; set; }
            public long sell_price_max { get; set; }
            public long buy_price_min { get; set; }
            public long buy_price_max { get; set; }
            public DateTime sell_price_min_date { get; set; }
            public DateTime sell_price_max_date { get; set; }
            public DateTime buy_price_min_date { get; set; }
            public DateTime buy_price_max_date { get; set; }
            public int sell_order_count { get; set; }
            public int buy_order_count { get; set; }
        }
    }

    public class BackupMarketProvider : IMarketDataProvider
    {
        private readonly ILogger _logger;

        public string ProviderName => "Backup Market Provider";

        public BackupMarketProvider(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<List<MarketItem>> FetchLatestDataAsync()
        {
            // Backup/fallback data
            await Task.Delay(100);
            return new List<MarketItem>();
        }

        public async Task<MarketItem> GetItemDataAsync(string itemId)
        {
            // Fallback implementation
            await Task.Delay(100);
            return null;
        }
    }

    // Supporting Data Models (additional to what's in CoreModels.cs)
    public class MarketUpdate
    {
        public string ItemId { get; set; }
        public long PreviousPrice { get; set; }
        public long NewPrice { get; set; }
        public DateTime UpdateTime { get; set; }
        public double ChangePercent { get; set; }
    }

    public class PriceAlert
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public long PreviousPrice { get; set; }
        public long NewPrice { get; set; }
        public double ChangePercent { get; set; }
        public AlertType AlertType { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class PriceHistory
    {
        public string ItemId { get; set; }
        public List<PricePoint> PricePoints { get; set; } = new();
    }

    public class PricePoint
    {
        public DateTime Timestamp { get; set; }
        public long SellPrice { get; set; }
        public long BuyPrice { get; set; }
        public int Volume { get; set; }
    }

    public class PriceAnalysis
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public long CurrentPrice { get; set; }
        public long EstimatedValue { get; set; }
        public long MarketPrice { get; set; }
        public long PriceDifference { get; set; }
        public double Confidence { get; set; }
        public string Recommendation { get; set; }
        public TrendDirection Trend { get; set; }
        public long Support { get; set; }
        public long Resistance { get; set; }
    }

    public class MarketTrends
    {
        public EntityType EntityType { get; set; }
        public int TierLevel { get; set; }
        public TrendDirection Trend { get; set; }
        public double AverageChange { get; set; }
        public double Confidence { get; set; }
        public int SampleSize { get; set; }
        public List<MarketItem> StrongTrendItems { get; set; } = new();
    }

    public class OpportunityAlert
    {
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public OpportunityType OpportunityType { get; set; }
        public double Score { get; set; }
        public List<string> Factors { get; set; } = new();
        public long CurrentPrice { get; set; }
        public string RecommendedAction { get; set; }
    }

    public class ProfitProjection
    {
        public long TotalEstimatedProfit { get; set; }
        public List<ItemProfitAnalysis> ItemAnalyses { get; set; } = new();
        public long ProfitPerHour { get; set; }
        public long RiskAdjustedProfit { get; set; }
        public double MarketVolatility { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public class ItemProfitAnalysis
    {
        public string ItemName { get; set; }
        public int Quantity { get; set; }
        public long MarketPrice { get; set; }
        public long EstimatedDrop { get; set; }
        public long Profit { get; set; }
        public double ProfitMargin { get; set; }
    }

    // Enums
    public enum TrendDirection
    {
        Unknown,
        Rising,
        Falling,
        Stable
    }

    public enum AlertType
    {
        PriceIncrease,
        PriceDecrease,
        VolumeSpike,
        MarketOpportunity
    }

    public enum OpportunityType
    {
        Low,
        Moderate,
        Good,
        Excellent
    }
}