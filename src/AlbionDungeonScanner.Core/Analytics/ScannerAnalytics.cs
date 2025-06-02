using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Services;
using Newtonsoft.Json;
using System.Text;

namespace AlbionDungeonScanner.Core.Analytics
{
    public class ScannerAnalytics
    {
        private readonly ILogger<ScannerAnalytics> _logger;
        private readonly DataPersistenceService _persistenceService;
        private readonly List<ScanSession> _sessions;
        private readonly Dictionary<string, EntityStatistics> _entityStats;
        private readonly Dictionary<DungeonType, DungeonAnalytics> _dungeonAnalytics;

        public ScannerAnalytics(ILogger<ScannerAnalytics> logger, DataPersistenceService persistenceService = null)
        {
            _logger = logger;
            _persistenceService = persistenceService;
            _sessions = new List<ScanSession>();
            _entityStats = new Dictionary<string, EntityStatistics>();
            _dungeonAnalytics = new Dictionary<DungeonType, DungeonAnalytics>();
            
            InitializeDungeonAnalytics();
        }

        private void InitializeDungeonAnalytics()
        {
            foreach (DungeonType dungeonType in Enum.GetValues<DungeonType>())
            {
                _dungeonAnalytics[dungeonType] = new DungeonAnalytics
                {
                    DungeonType = dungeonType,
                    TotalRuns = 0,
                    AverageValue = 0,
                    AverageTime = TimeSpan.Zero,
                    EntityFrequency = new Dictionary<string, int>(),
                    SuccessRate = 0.0,
                    TotalValue = 0,
                    TotalTime = TimeSpan.Zero,
                    EntityCount = 0
                };
            }
        }

        public async Task<ScanSession> StartNewSession()
        {
            var session = new ScanSession
            {
                SessionId = Guid.NewGuid(),
                StartTime = DateTime.UtcNow,
                DetectedEntities = new List<EntityDetection>(),
                AvalonianResults = new List<AvalonianScanResult>(),
                Performance = new SessionPerformance()
            };

            _sessions.Add(session);
            
            if (_persistenceService != null)
            {
                await _persistenceService.SaveSessionAsync(session);
            }
            
            _logger?.LogInformation($"Started new scan session: {session.SessionId}");
            return session;
        }

        public async Task EndSession(Guid sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null) return;

            session.EndTime = DateTime.UtcNow;
            session.Duration = session.EndTime.Value - session.StartTime;
            
            // Calculate session statistics
            CalculateSessionStatistics(session);
            
            // Update global analytics
            await UpdateGlobalAnalytics(session);
            
            if (_persistenceService != null)
            {
                await _persistenceService.SaveSessionAsync(session);
            }
            
            _logger?.LogInformation($"Ended scan session: {sessionId}, Duration: {session.Duration}");
        }

        public void RecordEntityDetection(DungeonEntity entity, ScanSession session)
        {
            var detection = new EntityDetection
            {
                DetectionId = Guid.NewGuid(),
                EntityId = entity.Id,
                EntityName = entity.Name,
                EntityType = entity.Type,
                DungeonType = entity.DungeonType,
                Position = entity.Position,
                DetectedAt = DateTime.UtcNow,
                SessionId = session.SessionId
            };

            session.DetectedEntities.Add(detection);
            UpdateEntityStatistics(entity);
            
            _logger?.LogDebug($"Recorded entity detection: {entity.Name} at {entity.Position}");
        }

        public void RecordAvalonianResult(AvalonianScanResult result, ScanSession session)
        {
            session.AvalonianResults.Add(result);
            
            // Update Avalonian-specific analytics
            var analytics = _dungeonAnalytics[DungeonType.Avalonian];
            analytics.TotalValue += result.EstimatedLoot.MaxSilver;
            analytics.EntityCount++;
            
            if (!analytics.EntityFrequency.ContainsKey(result.EntityData.Name))
                analytics.EntityFrequency[result.EntityData.Name] = 0;
            analytics.EntityFrequency[result.EntityData.Name]++;
        }

        private void UpdateEntityStatistics(DungeonEntity entity)
        {
            if (!_entityStats.ContainsKey(entity.Id))
            {
                _entityStats[entity.Id] = new EntityStatistics
                {
                    EntityId = entity.Id,
                    EntityName = entity.Name,
                    EntityType = entity.Type,
                    DungeonType = entity.DungeonType,
                    DetectionCount = 0,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    Positions = new List<Vector3>()
                };
            }

            var stats = _entityStats[entity.Id];
            stats.DetectionCount++;
            stats.LastSeen = DateTime.UtcNow;
            stats.Positions.Add(entity.Position);
        }

        private void CalculateSessionStatistics(ScanSession session)
        {
            session.Statistics = new SessionStatistics
            {
                TotalEntities = session.DetectedEntities.Count,
                AvalonianEntities = session.AvalonianResults.Count,
                EstimatedTotalValue = session.AvalonianResults.Sum(r => r.EstimatedLoot.MaxSilver),
                EstimatedFame = session.AvalonianResults.Sum(r => r.EstimatedLoot.Fame),
                UniqueEntityTypes = session.DetectedEntities.Select(e => e.EntityType).Distinct().Count(),
                MostCommonEntity = GetMostCommonEntity(session),
                HighestValueEntity = GetHighestValueEntity(session),
                AverageEntityValue = CalculateAverageEntityValue(session),
                DungeonEfficiency = CalculateDungeonEfficiency(session)
            };
        }

        private string GetMostCommonEntity(ScanSession session)
        {
            return session.DetectedEntities
                .GroupBy(e => e.EntityName)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "None";
        }

        private string GetHighestValueEntity(ScanSession session)
        {
            return session.AvalonianResults
                .OrderByDescending(r => r.EstimatedLoot.MaxSilver)
                .FirstOrDefault()?.EntityData.Name ?? "None";
        }

        private double CalculateAverageEntityValue(ScanSession session)
        {
            if (!session.AvalonianResults.Any()) return 0;
            return session.AvalonianResults.Average(r => r.EstimatedLoot.MaxSilver);
        }

        private double CalculateDungeonEfficiency(ScanSession session)
        {
            if (session.Duration.TotalMinutes == 0) return 0;
            
            var valuePerMinute = session.Statistics.EstimatedTotalValue / session.Duration.TotalMinutes;
            var famePerMinute = session.Statistics.EstimatedFame / session.Duration.TotalMinutes;
            
            // Combine value and fame for efficiency score
            return (valuePerMinute / 1000) + (famePerMinute / 100);
        }

        private async Task UpdateGlobalAnalytics(ScanSession session)
        {
            foreach (var detection in session.DetectedEntities)
            {
                var analytics = _dungeonAnalytics[detection.DungeonType];
                analytics.TotalRuns++;
                analytics.EntityCount += session.DetectedEntities.Count(e => e.DungeonType == detection.DungeonType);
                analytics.TotalTime = analytics.TotalTime.Add(session.Duration);
            }

            // Calculate averages
            foreach (var analytics in _dungeonAnalytics.Values)
            {
                if (analytics.TotalRuns > 0)
                {
                    analytics.AverageValue = analytics.TotalValue / analytics.TotalRuns;
                    analytics.AverageTime = TimeSpan.FromTicks(analytics.TotalTime.Ticks / analytics.TotalRuns);
                }
            }

            if (_persistenceService != null)
            {
                await _persistenceService.SaveAnalyticsAsync(_dungeonAnalytics);
            }
        }

        public AnalyticsReport GenerateSessionReport(Guid sessionId)
        {
            var session = _sessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null) return null;

            return new AnalyticsReport
            {
                ReportType = ReportType.Session,
                GeneratedAt = DateTime.UtcNow,
                SessionData = session,
                Summary = GenerateSessionSummary(session),
                Recommendations = GenerateRecommendations(session),
                Charts = GenerateChartData(session)
            };
        }

        public AnalyticsReport GenerateGlobalReport()
        {
            var report = new AnalyticsReport
            {
                ReportType = ReportType.Global,
                GeneratedAt = DateTime.UtcNow,
                GlobalData = new GlobalAnalytics
                {
                    TotalSessions = _sessions.Count,
                    TotalDetections = _sessions.Sum(s => s.DetectedEntities.Count),
                    TotalValue = _sessions.Sum(s => s.AvalonianResults.Sum(r => r.EstimatedLoot.MaxSilver)),
                    DungeonAnalytics = _dungeonAnalytics.Values.ToList(),
                    TopEntities = GetTopEntitiesByValue(),
                    EfficiencyTrends = CalculateEfficiencyTrends()
                },
                Summary = GenerateGlobalSummary(),
                Recommendations = GenerateGlobalRecommendations()
            };

            return report;
        }

        private string GenerateSessionSummary(ScanSession session)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Session Duration: {session.Duration:hh\\:mm\\:ss}");
            sb.AppendLine($"Entities Detected: {session.Statistics.TotalEntities}");
            sb.AppendLine($"Avalonian Entities: {session.Statistics.AvalonianEntities}");
            sb.AppendLine($"Estimated Value: {session.Statistics.EstimatedTotalValue:N0} silver");
            sb.AppendLine($"Estimated Fame: {session.Statistics.EstimatedFame:N0}");
            sb.AppendLine($"Efficiency Score: {session.Statistics.DungeonEfficiency:F2}");
            
            return sb.ToString();
        }

        private List<string> GenerateRecommendations(ScanSession session)
        {
            var recommendations = new List<string>();

            // Efficiency recommendations
            if (session.Statistics.DungeonEfficiency < 10)
            {
                recommendations.Add("Consider focusing on higher-tier Avalonian dungeons for better efficiency");
            }

            // Entity recommendations
            if (session.Statistics.AvalonianEntities < session.Statistics.TotalEntities * 0.3)
            {
                recommendations.Add("Look for more Avalonian content - they provide significantly higher rewards");
            }

            // Time recommendations
            if (session.Duration > TimeSpan.FromHours(2))
            {
                recommendations.Add("Consider shorter, more focused runs for better silver/hour ratio");
            }

            // Value recommendations
            if (session.Statistics.AverageEntityValue < 5000)
            {
                recommendations.Add("Target higher-tier chests and bosses for better value per entity");
            }

            return recommendations;
        }

        private ChartData GenerateChartData(ScanSession session)
        {
            return new ChartData
            {
                EntityTypeDistribution = session.DetectedEntities
                    .GroupBy(e => e.EntityType)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                    
                ValueOverTime = session.AvalonianResults
                    .OrderBy(r => r.Position.X) // Proxy for time progression
                    .Select((r, i) => new { Time = i, Value = r.EstimatedLoot.MaxSilver })
                    .ToDictionary(x => x.Time, x => (long)x.Value),
                    
                DungeonTypeDistribution = session.DetectedEntities
                    .GroupBy(e => e.DungeonType)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count())
            };
        }

        private string GenerateGlobalSummary()
        {
            var totalValue = _sessions.Sum(s => s.AvalonianResults.Sum(r => r.EstimatedLoot.MaxSilver));
            var totalTime = _sessions.Sum(s => s.Duration.TotalHours);
            var avgEfficiency = totalTime > 0 ? totalValue / totalTime : 0;

            var sb = new StringBuilder();
            sb.AppendLine($"Total Sessions: {_sessions.Count}");
            sb.AppendLine($"Total Value Detected: {totalValue:N0} silver");
            sb.AppendLine($"Total Scan Time: {totalTime:F1} hours");
            sb.AppendLine($"Average Silver/Hour: {avgEfficiency:N0}");
            sb.AppendLine($"Most Valuable Dungeon Type: {GetMostValuableDungeonType()}");
            
            return sb.ToString();
        }

        private List<string> GenerateGlobalRecommendations()
        {
            var recommendations = new List<string>();
            
            var bestDungeonType = GetMostValuableDungeonType();
            recommendations.Add($"Focus on {bestDungeonType} dungeons for optimal returns");
            
            if (_sessions.Any())
            {
                var avgSessionLength = TimeSpan.FromTicks(_sessions.Average(s => s.Duration.Ticks));
                if (avgSessionLength > TimeSpan.FromHours(1.5))
                {
                    recommendations.Add("Consider shorter sessions for better focus and efficiency");
                }
            }
            
            return recommendations;
        }

        private List<EntityValueData> GetTopEntitiesByValue()
        {
            return _entityStats.Values
                .Where(e => e.EntityType == EntityType.Chest || e.EntityType == EntityType.Boss)
                .OrderByDescending(e => e.DetectionCount)
                .Take(10)
                .Select(e => new EntityValueData
                {
                    EntityName = e.EntityName,
                    DetectionCount = e.DetectionCount,
                    AverageValue = CalculateAverageEntityValue(e.EntityId)
                })
                .ToList();
        }

        private List<EfficiencyTrendData> CalculateEfficiencyTrends()
        {
            return _sessions
                .OrderBy(s => s.StartTime)
                .Select((s, i) => new EfficiencyTrendData
                {
                    SessionNumber = i + 1,
                    Efficiency = s.Statistics?.DungeonEfficiency ?? 0,
                    Date = s.StartTime.Date
                })
                .ToList();
        }

        private string GetMostValuableDungeonType()
        {
            return _dungeonAnalytics.Values
                .OrderByDescending(d => d.AverageValue)
                .FirstOrDefault()?.DungeonType.ToString() ?? "Unknown";
        }

        private double CalculateAverageEntityValue(string entityId)
        {
            var detections = _sessions
                .SelectMany(s => s.AvalonianResults)
                .Where(r => r.EntityData.Name.Contains(entityId))
                .ToList();
                
            return detections.Any() ? detections.Average(r => r.EstimatedLoot.MaxSilver) : 0;
        }

        // Export Methods
        public async Task ExportToJson(AnalyticsReport report, string filePath)
        {
            var json = JsonConvert.SerializeObject(report, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task ExportToCsv(List<ScanSession> sessions, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("SessionId,StartTime,Duration,TotalEntities,AvalonianEntities,EstimatedValue,Efficiency");
            
            foreach (var session in sessions)
            {
                csv.AppendLine($"{session.SessionId},{session.StartTime:yyyy-MM-dd HH:mm:ss}," +
                             $"{session.Duration:hh\\:mm\\:ss},{session.Statistics?.TotalEntities ?? 0}," +
                             $"{session.Statistics?.AvalonianEntities ?? 0},{session.Statistics?.EstimatedTotalValue ?? 0}," +
                             $"{session.Statistics?.DungeonEfficiency ?? 0:F2}");
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
        }

        public ScanSession GetCurrentSession()
        {
            return _sessions.LastOrDefault(s => s.EndTime == null);
        }

        public List<ScanSession> GetRecentSessions(int count = 10)
        {
            return _sessions.OrderByDescending(s => s.StartTime).Take(count).ToList();
        }

        public Dictionary<string, object> GetQuickStats()
        {
            var currentSession = GetCurrentSession();
            var recentSessions = GetRecentSessions(5);

            return new Dictionary<string, object>
            {
                ["CurrentSessionEntities"] = currentSession?.DetectedEntities.Count ?? 0,
                ["CurrentSessionValue"] = currentSession?.AvalonianResults.Sum(r => r.EstimatedLoot.MaxSilver) ?? 0,
                ["RecentSessionsCount"] = recentSessions.Count,
                ["AverageEfficiency"] = recentSessions.Any() ? recentSessions.Average(s => s.Statistics?.DungeonEfficiency ?? 0) : 0,
                ["TotalLifetimeValue"] = _sessions.Sum(s => s.AvalonianResults.Sum(r => r.EstimatedLoot.MaxSilver))
            };
        }
    }

    // Supporting Data Models
    public class DungeonAnalytics
    {
        public DungeonType DungeonType { get; set; }
        public int TotalRuns { get; set; }
        public long TotalValue { get; set; }
        public double AverageValue { get; set; }
        public TimeSpan TotalTime { get; set; }
        public TimeSpan AverageTime { get; set; }
        public int EntityCount { get; set; }
        public Dictionary<string, int> EntityFrequency { get; set; } = new();
        public double SuccessRate { get; set; }
    }

    public class AnalyticsReport
    {
        public ReportType ReportType { get; set; }
        public DateTime GeneratedAt { get; set; }
        public ScanSession SessionData { get; set; }
        public GlobalAnalytics GlobalData { get; set; }
        public string Summary { get; set; }
        public List<string> Recommendations { get; set; } = new();
        public ChartData Charts { get; set; }
    }

    public class GlobalAnalytics
    {
        public int TotalSessions { get; set; }
        public int TotalDetections { get; set; }
        public long TotalValue { get; set; }
        public List<DungeonAnalytics> DungeonAnalytics { get; set; } = new();
        public List<EntityValueData> TopEntities { get; set; } = new();
        public List<EfficiencyTrendData> EfficiencyTrends { get; set; } = new();
    }

    public class ChartData
    {
        public Dictionary<string, int> EntityTypeDistribution { get; set; } = new();
        public Dictionary<int, long> ValueOverTime { get; set; } = new();
        public Dictionary<string, int> DungeonTypeDistribution { get; set; } = new();
    }

    public class EntityValueData
    {
        public string EntityName { get; set; }
        public int DetectionCount { get; set; }
        public double AverageValue { get; set; }
    }

    public class EfficiencyTrendData
    {
        public int SessionNumber { get; set; }
        public double Efficiency { get; set; }
        public DateTime Date { get; set; }
    }

    public class EntityStatistics
    {
        public string EntityId { get; set; }
        public string EntityName { get; set; }
        public EntityType EntityType { get; set; }
        public DungeonType DungeonType { get; set; }
        public int DetectionCount { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public List<Vector3> Positions { get; set; } = new();
    }

    public enum ReportType
    {
        Session,
        Global,
        Comparative,
        DungeonSpecific
    }
}