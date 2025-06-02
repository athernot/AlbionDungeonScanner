using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Services;

namespace AlbionDungeonScanner.Core.Performance
{
    public class PerformanceManager
    {
        private readonly ILogger<PerformanceManager> _logger;
        private readonly DataPersistenceService _persistenceService;
        private readonly PerformanceCounter _cpuCounter;
        private readonly Timer _performanceTimer;
        private readonly ConcurrentQueue<PerformanceMetric> _metricsHistory;
        private readonly object _lockObject = new object();
        private readonly Process _currentProcess;

        public PerformanceMetrics CurrentMetrics { get; private set; }
        public bool IsMonitoring { get; private set; }

        public event Action<PerformanceMetrics> PerformanceUpdated;
        public event Action<PerformanceAlert> PerformanceAlertTriggered;

        public PerformanceManager(ILogger<PerformanceManager> logger = null, DataPersistenceService persistenceService = null)
        {
            _logger = logger;
            _persistenceService = persistenceService;
            _metricsHistory = new ConcurrentQueue<PerformanceMetric>();
            _currentProcess = Process.GetCurrentProcess();

            try
            {
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _currentProcess.ProcessName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "CPU performance counter not available");
            }

            CurrentMetrics = new PerformanceMetrics();
            _performanceTimer = new Timer(UpdatePerformanceMetrics, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void StartMonitoring()
        {
            if (IsMonitoring) return;

            IsMonitoring = true;
            _performanceTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
            _logger?.LogInformation("Performance monitoring started");
        }

        public void StopMonitoring()
        {
            if (!IsMonitoring) return;

            IsMonitoring = false;
            _performanceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger?.LogInformation("Performance monitoring stopped");
        }

        private async void UpdatePerformanceMetrics(object state)
        {
            try
            {
                lock (_lockObject)
                {
                    _currentProcess.Refresh();
                    
                    CurrentMetrics = new PerformanceMetrics
                    {
                        Timestamp = DateTime.UtcNow,
                        CpuUsage = GetCpuUsage(),
                        MemoryUsage = _currentProcess.WorkingSet64 / 1024 / 1024, // MB
                        AvailableMemory = GetAvailableMemory(),
                        ThreadCount = _currentProcess.Threads.Count,
                        HandleCount = _currentProcess.HandleCount,
                        PacketsPerSecond = PacketProcessor.Instance?.PacketsPerSecond ?? 0,
                        EntitiesDetectedPerSecond = EntityDetectionRate.Instance?.EntitiesPerSecond ?? 0
                    };

                    // Keep metrics history for trending
                    _metricsHistory.Enqueue(new PerformanceMetric
                    {
                        Timestamp = CurrentMetrics.Timestamp,
                        Value = CurrentMetrics.CpuUsage,
                        MetricType = MetricType.CpuUsage
                    });

                    _metricsHistory.Enqueue(new PerformanceMetric
                    {
                        Timestamp = CurrentMetrics.Timestamp,
                        Value = CurrentMetrics.MemoryUsage,
                        MetricType = MetricType.MemoryUsage
                    });

                    // Keep only last 200 metrics (40 minutes at 5-second intervals)
                    while (_metricsHistory.Count > 200)
                    {
                        _metricsHistory.TryDequeue(out _);
                    }

                    PerformanceUpdated?.Invoke(CurrentMetrics);
                    CheckPerformanceThresholds();

                    // Save to database periodically
                    if (_persistenceService != null && CurrentMetrics.Timestamp.Second % 30 == 0)
                    {
                        _ = Task.Run(() => _persistenceService.SavePerformanceMetricAsync(CurrentMetrics));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating performance metrics");
            }
        }

        private double GetCpuUsage()
        {
            try
            {
                if (_cpuCounter != null)
                {
                    var cpuUsage = _cpuCounter.NextValue();
                    // Normalize to percentage (0-100)
                    return Math.Min(100, Math.Max(0, cpuUsage));
                }
                
                // Fallback: estimate CPU usage based on processor time
                var totalTime = _currentProcess.TotalProcessorTime.TotalMilliseconds;
                var elapsedTime = (DateTime.UtcNow - _currentProcess.StartTime).TotalMilliseconds;
                var cpuCores = Environment.ProcessorCount;
                
                return Math.Min(100, (totalTime / elapsedTime / cpuCores) * 100);
            }
            catch
            {
                return 0.0;
            }
        }

        private long GetAvailableMemory()
        {
            try
            {
                var gc = GC.GetTotalMemory(false);
                var workingSet = _currentProcess.WorkingSet64;
                var privateMemory = _currentProcess.PrivateMemorySize64;
                
                // Estimate available memory (simplified)
                return Math.Max(0, (privateMemory - workingSet) / 1024 / 1024);
            }
            catch
            {
                return 0;
            }
        }

        private void CheckPerformanceThresholds()
        {
            var config = PerformanceConfiguration.Default;
            var alerts = new List<PerformanceAlert>();
            
            if (CurrentMetrics.CpuUsage > config.CpuThreshold)
            {
                alerts.Add(new PerformanceAlert
                {
                    Type = PerformanceAlertType.HighCpuUsage,
                    Severity = GetSeverity(CurrentMetrics.CpuUsage, config.CpuThreshold),
                    Message = $"High CPU usage detected: {CurrentMetrics.CpuUsage:F1}%",
                    CurrentValue = CurrentMetrics.CpuUsage,
                    Threshold = config.CpuThreshold,
                    Timestamp = DateTime.UtcNow,
                    Recommendation = "Consider reducing packet processing rate or closing other applications"
                });
                
                TriggerPerformanceOptimization(OptimizationType.ReduceCpuUsage);
            }

            if (CurrentMetrics.MemoryUsage > config.MemoryThreshold)
            {
                alerts.Add(new PerformanceAlert
                {
                    Type = PerformanceAlertType.HighMemoryUsage,
                    Severity = GetSeverity(CurrentMetrics.MemoryUsage, config.MemoryThreshold),
                    Message = $"High memory usage detected: {CurrentMetrics.MemoryUsage} MB",
                    CurrentValue = CurrentMetrics.MemoryUsage,
                    Threshold = config.MemoryThreshold,
                    Timestamp = DateTime.UtcNow,
                    Recommendation = "Consider clearing entity cache or reducing data retention period"
                });
                
                TriggerPerformanceOptimization(OptimizationType.ReduceMemoryUsage);
            }

            if (CurrentMetrics.PacketsPerSecond > 1000) // High packet rate
            {
                alerts.Add(new PerformanceAlert
                {
                    Type = PerformanceAlertType.HighPacketRate,
                    Severity = AlertSeverity.Warning,
                    Message = $"High packet processing rate: {CurrentMetrics.PacketsPerSecond:F0} packets/sec",
                    CurrentValue = CurrentMetrics.PacketsPerSecond,
                    Threshold = 1000,
                    Timestamp = DateTime.UtcNow,
                    Recommendation = "Consider enabling packet filtering or reducing buffer size"
                });
            }

            if (CurrentMetrics.ThreadCount > 50) // Too many threads
            {
                alerts.Add(new PerformanceAlert
                {
                    Type = PerformanceAlertType.HighThreadCount,
                    Severity = AlertSeverity.Warning,
                    Message = $"High thread count: {CurrentMetrics.ThreadCount}",
                    CurrentValue = CurrentMetrics.ThreadCount,
                    Threshold = 50,
                    Timestamp = DateTime.UtcNow,
                    Recommendation = "Review concurrent operations and thread pool usage"
                });
            }

            foreach (var alert in alerts)
            {
                PerformanceAlertTriggered?.Invoke(alert);
                _logger?.LogWarning("Performance alert: {Message}", alert.Message);
            }
        }

        private AlertSeverity GetSeverity(double currentValue, double threshold)
        {
            var ratio = currentValue / threshold;
            
            if (ratio > 2.0) return AlertSeverity.Critical;
            if (ratio > 1.5) return AlertSeverity.High;
            if (ratio > 1.2) return AlertSeverity.Warning;
            
            return AlertSeverity.Info;
        }

        private void TriggerPerformanceOptimization(OptimizationType type)
        {
            try
            {
                switch (type)
                {
                    case OptimizationType.ReduceCpuUsage:
                        PacketProcessor.Instance?.ReduceProcessingRate();
                        // Add small delay to processing
                        Thread.Sleep(10);
                        break;
                        
                    case OptimizationType.ReduceMemoryUsage:
                        EntityCache.Instance?.ClearOldEntries();
                        // Force garbage collection
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        break;
                        
                    case OptimizationType.OptimizeNetworkProcessing:
                        PacketProcessor.Instance?.ReduceProcessingRate();
                        break;
                }
                
                _logger?.LogInformation("Triggered performance optimization: {Type}", type);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during performance optimization");
            }
        }

        public List<PerformanceMetric> GetMetricsHistory(MetricType metricType, TimeSpan timeRange)
        {
            var cutoff = DateTime.UtcNow - timeRange;
            return _metricsHistory
                .Where(m => m.MetricType == metricType && m.Timestamp >= cutoff)
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        public PerformanceSummary GetPerformanceSummary(TimeSpan timeRange)
        {
            var cutoff = DateTime.UtcNow - timeRange;
            var recentMetrics = _metricsHistory
                .Where(m => m.Timestamp >= cutoff)
                .ToList();

            if (!recentMetrics.Any())
            {
                return new PerformanceSummary
                {
                    TimeRange = timeRange,
                    SampleCount = 0
                };
            }

            var cpuMetrics = recentMetrics.Where(m => m.MetricType == MetricType.CpuUsage).Select(m => m.Value).ToList();
            var memoryMetrics = recentMetrics.Where(m => m.MetricType == MetricType.MemoryUsage).Select(m => m.Value).ToList();

            return new PerformanceSummary
            {
                TimeRange = timeRange,
                SampleCount = recentMetrics.Count,
                AverageCpuUsage = cpuMetrics.Any() ? cpuMetrics.Average() : 0,
                MaxCpuUsage = cpuMetrics.Any() ? cpuMetrics.Max() : 0,
                AverageMemoryUsage = memoryMetrics.Any() ? memoryMetrics.Average() : 0,
                MaxMemoryUsage = memoryMetrics.Any() ? memoryMetrics.Max() : 0,
                CurrentCpuUsage = CurrentMetrics.CpuUsage,
                CurrentMemoryUsage = CurrentMetrics.MemoryUsage
            };
        }

        public async Task<List<PerformanceRecommendation>> GetPerformanceRecommendations()
        {
            var recommendations = new List<PerformanceRecommendation>();
            var summary = GetPerformanceSummary(TimeSpan.FromMinutes(30));

            // CPU recommendations
            if (summary.AverageCpuUsage > 70)
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Category = "CPU",
                    Priority = RecommendationPriority.High,
                    Title = "High CPU Usage Detected",
                    Description = $"Average CPU usage is {summary.AverageCpuUsage:F1}% over the last 30 minutes",
                    Recommendations = new[]
                    {
                        "Reduce packet processing buffer size",
                        "Enable packet filtering for non-essential traffic",
                        "Close other CPU-intensive applications",
                        "Consider running scanner during off-peak hours"
                    }
                });
            }

            // Memory recommendations
            if (summary.AverageMemoryUsage > 1024) // 1GB
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Category = "Memory",
                    Priority = RecommendationPriority.Medium,
                    Title = "High Memory Usage",
                    Description = $"Average memory usage is {summary.AverageMemoryUsage:F0} MB",
                    Recommendations = new[]
                    {
                        "Reduce data retention period",
                        "Clear entity cache more frequently",
                        "Disable detailed logging if not needed",
                        "Restart scanner periodically to free memory"
                    }
                });
            }

            // Performance trend recommendations
            var recentCpuTrend = GetTrend(MetricType.CpuUsage, TimeSpan.FromMinutes(15));
            if (recentCpuTrend > 10) // Increasing trend
            {
                recommendations.Add(new PerformanceRecommendation
                {
                    Category = "Trend",
                    Priority = RecommendationPriority.Medium,
                    Title = "Increasing CPU Usage Trend",
                    Description = "CPU usage has been steadily increasing",
                    Recommendations = new[]
                    {
                        "Monitor for potential memory leaks",
                        "Check for background processes",
                        "Consider restarting the scanner"
                    }
                });
            }

            return recommendations;
        }

        private double GetTrend(MetricType metricType, TimeSpan timeRange)
        {
            var metrics = GetMetricsHistory(metricType, timeRange);
            if (metrics.Count < 2) return 0;

            var firstValue = metrics.First().Value;
            var lastValue = metrics.Last().Value;
            
            return ((lastValue - firstValue) / firstValue) * 100;
        }

        public Dictionary<string, object> GetDetailedStatistics()
        {
            _currentProcess.Refresh();
            
            return new Dictionary<string, object>
            {
                ["ProcessId"] = _currentProcess.Id,
                ["ProcessName"] = _currentProcess.ProcessName,
                ["StartTime"] = _currentProcess.StartTime,
                ["TotalProcessorTime"] = _currentProcess.TotalProcessorTime,
                ["WorkingSet64"] = _currentProcess.WorkingSet64,
                ["PrivateMemorySize64"] = _currentProcess.PrivateMemorySize64,
                ["VirtualMemorySize64"] = _currentProcess.VirtualMemorySize64,
                ["PagedMemorySize64"] = _currentProcess.PagedMemorySize64,
                ["ThreadCount"] = _currentProcess.Threads.Count,
                ["HandleCount"] = _currentProcess.HandleCount,
                ["GCTotalMemory"] = GC.GetTotalMemory(false),
                ["GCGen0Collections"] = GC.CollectionCount(0),
                ["GCGen1Collections"] = GC.CollectionCount(1),
                ["GCGen2Collections"] = GC.CollectionCount(2),
                ["SystemInfo"] = GetSystemInfo()
            };
        }

        private Dictionary<string, object> GetSystemInfo()
        {
            return new Dictionary<string, object>
            {
                ["ProcessorCount"] = Environment.ProcessorCount,
                ["OSVersion"] = Environment.OSVersion.ToString(),
                ["WorkingSet"] = Environment.WorkingSet,
                ["Is64BitProcess"] = Environment.Is64BitProcess,
                ["Is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem,
                ["CLRVersion"] = Environment.Version.ToString()
            };
        }

        public void Dispose()
        {
            StopMonitoring();
            _performanceTimer?.Dispose();
            _cpuCounter?.Dispose();
            _currentProcess?.Dispose();
        }
    }

    // Supporting Classes
    public class PerformanceMetric
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public MetricType MetricType { get; set; }
    }

    public class PerformanceAlert
    {
        public PerformanceAlertType Type { get; set; }
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; }
        public double CurrentValue { get; set; }
        public double Threshold { get; set; }
        public DateTime Timestamp { get; set; }
        public string Recommendation { get; set; }
    }

    public class PerformanceSummary
    {
        public TimeSpan TimeRange { get; set; }
        public int SampleCount { get; set; }
        public double AverageCpuUsage { get; set; }
        public double MaxCpuUsage { get; set; }
        public double AverageMemoryUsage { get; set; }
        public double MaxMemoryUsage { get; set; }
        public double CurrentCpuUsage { get; set; }
        public double CurrentMemoryUsage { get; set; }
    }

    public class PerformanceRecommendation
    {
        public string Category { get; set; }
        public RecommendationPriority Priority { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string[] Recommendations { get; set; }
    }

    public enum MetricType
    {
        CpuUsage,
        MemoryUsage,
        PacketsPerSecond,
        EntitiesPerSecond
    }

    public enum OptimizationType
    {
        ReduceCpuUsage,
        ReduceMemoryUsage,
        OptimizeNetworkProcessing
    }

    public enum PerformanceAlertType
    {
        HighCpuUsage,
        HighMemoryUsage,
        HighPacketRate,
        HighThreadCount,
        LowDiskSpace,
        NetworkLatency
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        High,
        Critical
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }

    public static class PerformanceConfiguration
    {
        public static PerformanceConfig Default => new PerformanceConfig
        {
            CpuThreshold = 80.0,
            MemoryThreshold = 1024, // MB
            MaxQueueSize = 10000,
            OptimizationEnabled = true
        };
    }

    public class PerformanceConfig
    {
        public double CpuThreshold { get; set; }
        public long MemoryThreshold { get; set; }
        public int MaxQueueSize { get; set; }
        public bool OptimizationEnabled { get; set; }
    }

    // Singleton classes for tracking
    public class PacketProcessor
    {
        private static readonly Lazy<PacketProcessor> _instance = new Lazy<PacketProcessor>(() => new PacketProcessor());
        public static PacketProcessor Instance => _instance.Value;
        
        private volatile double _packetsPerSecond;
        private int _processingRate = 100;
        
        public double PacketsPerSecond 
        { 
            get => _packetsPerSecond; 
            set => _packetsPerSecond = value; 
        }
        
        public int QueueSize { get; set; }

        public void ReduceProcessingRate()
        {
            _processingRate = Math.Max(10, _processingRate - 20);
        }

        public void IncreaseProcessingRate()
        {
            _processingRate = Math.Min(500, _processingRate + 20);
        }
    }

    public class EntityDetectionRate
    {
        private static readonly Lazy<EntityDetectionRate> _instance = new Lazy<EntityDetectionRate>();
        public static EntityDetectionRate Instance => _instance.Value;
        
        public double EntitiesPerSecond { get; set; }
    }

    public class EntityCache
    {
        private static readonly Lazy<EntityCache> _instance = new Lazy<EntityCache>();
        public static EntityCache Instance => _instance.Value;
        
        private readonly ConcurrentDictionary<string, CachedEntity> _cache = new();

        public void ClearOldEntries()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-30);
            var keysToRemove = _cache
                .Where(kvp => kvp.Value.LastAccessed < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }

        private class CachedEntity
        {
            public DateTime LastAccessed { get; set; }
            public object Data { get; set; }
        }
    }
}