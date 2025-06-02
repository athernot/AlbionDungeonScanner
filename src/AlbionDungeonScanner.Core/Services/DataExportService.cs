using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Analytics;
using Newtonsoft.Json;
using System.Globalization;

namespace AlbionDungeonScanner.Core.Services
{
    public class DataExportService
    {
        private readonly DataPersistenceService _persistenceService;
        private readonly ScannerAnalytics _analytics;
        private readonly ILogger<DataExportService> _logger;

        public DataExportService(
            DataPersistenceService persistenceService = null, 
            ScannerAnalytics analytics = null, 
            ILogger<DataExportService> logger = null)
        {
            _persistenceService = persistenceService;
            _analytics = analytics;
            _logger = logger;
        }

        public async Task ExportCurrentSession()
        {
            var exportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                                        $"AlbionScan_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(exportPath);

            try
            {
                _logger?.LogInformation($"Starting data export to {exportPath}");

                if (_persistenceService != null)
                {
                    // Export recent sessions
                    var sessions = await _persistenceService.GetSessionsAsync(DateTime.UtcNow.AddDays(-7));
                    await ExportSessionsToCsv(sessions, Path.Combine(exportPath, "sessions.csv"));
                    await ExportSessionsToJson(sessions, Path.Combine(exportPath, "sessions.json"));

                    // Export performance metrics
                    var performanceMetrics = await _persistenceService.GetPerformanceMetricsAsync(DateTime.UtcNow.AddDays(-1), 1000);
                    await ExportPerformanceMetricsToCsv(performanceMetrics, Path.Combine(exportPath, "performance_metrics.csv"));
                }

                if (_analytics != null)
                {
                    // Export analytics report
                    var report = _analytics.GenerateGlobalReport();
                    await ExportAnalyticsReport(report, Path.Combine(exportPath, "analytics_report.json"));

                    // Export detailed analytics
                    await ExportDetailedAnalytics(Path.Combine(exportPath, "detailed_analytics.csv"));
                }

                // Create summary file
                await CreateSummaryFile(exportPath);

                _logger?.LogInformation($"Data exported successfully to {exportPath}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export data");
                throw;
            }
        }

        public async Task ExportSessionsToCsv(List<ScanSession> sessions, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("SessionId,StartTime,EndTime,Duration,TotalEntities,AvalonianEntities,EstimatedValue,EstimatedFame,Efficiency,MostCommonEntity,HighestValueEntity");
            
            foreach (var session in sessions.OrderBy(s => s.StartTime))
            {
                csv.AppendLine($"{session.SessionId}," +
                             $"{session.StartTime:yyyy-MM-dd HH:mm:ss}," +
                             $"{session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}," +
                             $"{session.Duration:hh\\:mm\\:ss}," +
                             $"{session.Statistics?.TotalEntities ?? 0}," +
                             $"{session.Statistics?.AvalonianEntities ?? 0}," +
                             $"{session.Statistics?.EstimatedTotalValue ?? 0}," +
                             $"{session.Statistics?.EstimatedFame ?? 0}," +
                             $"{session.Statistics?.DungeonEfficiency ?? 0:F2}," +
                             $"\"{session.Statistics?.MostCommonEntity ?? ""}\"," +
                             $"\"{session.Statistics?.HighestValueEntity ?? ""}\"");
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
            _logger?.LogDebug($"Exported {sessions.Count} sessions to CSV");
        }

        public async Task ExportSessionsToJson(List<ScanSession> sessions, string filePath)
        {
            var json = JsonConvert.SerializeObject(sessions, Formatting.Indented, new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-dd HH:mm:ss",
                NullValueHandling = NullValueHandling.Ignore
            });
            
            await File.WriteAllTextAsync(filePath, json);
            _logger?.LogDebug($"Exported {sessions.Count} sessions to JSON");
        }

        public async Task ExportEntityDetectionsToCsv(List<ScanSession> sessions, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("SessionId,DetectionId,EntityId,EntityName,EntityType,DungeonType,PositionX,PositionY,PositionZ,DetectedAt");
            
            foreach (var session in sessions)
            {
                foreach (var detection in session.DetectedEntities.OrderBy(d => d.DetectedAt))
                {
                    csv.AppendLine($"{session.SessionId}," +
                                 $"{detection.DetectionId}," +
                                 $"\"{detection.EntityId}\"," +
                                 $"\"{detection.EntityName}\"," +
                                 $"{detection.EntityType}," +
                                 $"{detection.DungeonType}," +
                                 $"{detection.Position.X:F2}," +
                                 $"{detection.Position.Y:F2}," +
                                 $"{detection.Position.Z:F2}," +
                                 $"{detection.DetectedAt:yyyy-MM-dd HH:mm:ss}");
                }
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
            _logger?.LogDebug($"Exported entity detections to CSV");
        }

        public async Task ExportAvalonianResultsToCsv(List<ScanSession> sessions, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("SessionId,EntityName,EntityType,Tier,EstimatedMinValue,EstimatedMaxValue,Fame,ThreatLevel,Priority,PositionX,PositionY,PositionZ,RecommendedStrategy");
            
            foreach (var session in sessions)
            {
                foreach (var result in session.AvalonianResults)
                {
                    csv.AppendLine($"{session.SessionId}," +
                                 $"\"{result.EntityData.Name}\"," +
                                 $"{result.EntityData.Type}," +
                                 $"{result.EntityData.Tier}," +
                                 $"{result.EstimatedLoot.MinSilver}," +
                                 $"{result.EstimatedLoot.MaxSilver}," +
                                 $"{result.EstimatedLoot.Fame}," +
                                 $"{result.ThreatLevel}," +
                                 $"{result.EntityData.Priority}," +
                                 $"{result.Position.X:F2}," +
                                 $"{result.Position.Y:F2}," +
                                 $"{result.Position.Z:F2}," +
                                 $"\"{result.RecommendedStrategy?.Replace("\"", "\"\"") ?? ""}\"");
                }
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
            _logger?.LogDebug($"Exported Avalonian results to CSV");
        }

        public async Task ExportPerformanceMetricsToCsv(List<PerformanceMetrics> metrics, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,CpuUsage,MemoryUsageMB,AvailableMemoryMB,ThreadCount,HandleCount,PacketsPerSecond,EntitiesPerSecond");
            
            foreach (var metric in metrics.OrderBy(m => m.Timestamp))
            {
                csv.AppendLine($"{metric.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                             $"{metric.CpuUsage:F2}," +
                             $"{metric.MemoryUsage}," +
                             $"{metric.AvailableMemory}," +
                             $"{metric.ThreadCount}," +
                             $"{metric.HandleCount}," +
                             $"{metric.PacketsPerSecond:F2}," +
                             $"{metric.EntitiesDetectedPerSecond:F2}");
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
            _logger?.LogDebug($"Exported {metrics.Count} performance metrics to CSV");
        }

        public async Task ExportAnalyticsReport(AnalyticsReport report, string filePath)
        {
            var json = JsonConvert.SerializeObject(report, Formatting.Indented, new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-dd HH:mm:ss",
                NullValueHandling = NullValueHandling.Ignore
            });
            
            await File.WriteAllTextAsync(filePath, json);
            _logger?.LogDebug("Exported analytics report to JSON");
        }

        public async Task ExportDetailedAnalytics(string filePath)
        {
            if (_analytics == null) return;

            var report = _analytics.GenerateGlobalReport();
            var csv = new StringBuilder();
            
            // Header
            csv.AppendLine("Metric,Value,Category");
            
            // Global statistics
            csv.AppendLine($"Total Sessions,{report.GlobalData.TotalSessions},Global");
            csv.AppendLine($"Total Detections,{report.GlobalData.TotalDetections},Global");
            csv.AppendLine($"Total Value,{report.GlobalData.TotalValue:N0},Global");
            
            // Dungeon analytics
            foreach (var dungeonAnalytic in report.GlobalData.DungeonAnalytics)
            {
                csv.AppendLine($"{dungeonAnalytic.DungeonType} - Total Runs,{dungeonAnalytic.TotalRuns},Dungeon");
                csv.AppendLine($"{dungeonAnalytic.DungeonType} - Average Value,{dungeonAnalytic.AverageValue:F0},Dungeon");
                csv.AppendLine($"{dungeonAnalytic.DungeonType} - Average Time,{dungeonAnalytic.AverageTime:hh\\:mm\\:ss},Dungeon");
                csv.AppendLine($"{dungeonAnalytic.DungeonType} - Entity Count,{dungeonAnalytic.EntityCount},Dungeon");
            }
            
            // Top entities
            foreach (var entity in report.GlobalData.TopEntities.Take(10))
            {
                csv.AppendLine($"{entity.EntityName} - Detections,{entity.DetectionCount},TopEntity");
                csv.AppendLine($"{entity.EntityName} - Average Value,{entity.AverageValue:F0},TopEntity");
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
            _logger?.LogDebug("Exported detailed analytics to CSV");
        }

        public async Task ExportCustomData<T>(IEnumerable<T> data, string filePath, Func<T, string> csvFormatter)
        {
            var csv = new StringBuilder();
            
            foreach (var item in data)
            {
                csv.AppendLine(csvFormatter(item));
            }
            
            await File.WriteAllTextAsync(filePath, csv.ToString());
            _logger?.LogDebug($"Exported custom data to {filePath}");
        }

        private async Task CreateSummaryFile(string exportPath)
        {
            var summaryPath = Path.Combine(exportPath, "export_summary.txt");
            var sb = new StringBuilder();
            
            sb.AppendLine("Albion Online Dungeon Scanner - Export Summary");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=" + new string('=', 50));
            sb.AppendLine();

            // Get file information
            var files = Directory.GetFiles(exportPath);
            sb.AppendLine($"Total Files: {files.Length}");
            sb.AppendLine();
            
            foreach (var file in files.OrderBy(f => f))
            {
                var fileInfo = new FileInfo(file);
                sb.AppendLine($"{fileInfo.Name} - {fileInfo.Length:N0} bytes");
            }
            
            sb.AppendLine();
            sb.AppendLine("File Descriptions:");
            sb.AppendLine("- sessions.csv: Summary of all scan sessions");
            sb.AppendLine("- sessions.json: Detailed session data in JSON format");
            sb.AppendLine("- performance_metrics.csv: System performance data");
            sb.AppendLine("- analytics_report.json: Comprehensive analytics report");
            sb.AppendLine("- detailed_analytics.csv: Detailed metrics breakdown");
            
            await File.WriteAllTextAsync(summaryPath, sb.ToString());
        }

        public async Task ExportToExcel(List<ScanSession> sessions, string filePath)
        {
            // This would require a library like EPPlus or ClosedXML
            // For now, we'll export multiple CSV files that can be imported into Excel
            
            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            
            await ExportSessionsToCsv(sessions, Path.Combine(directory, $"{fileName}_sessions.csv"));
            await ExportEntityDetectionsToCsv(sessions, Path.Combine(directory, $"{fileName}_detections.csv"));
            await ExportAvalonianResultsToCsv(sessions, Path.Combine(directory, $"{fileName}_avalonian.csv"));
            
            _logger?.LogInformation($"Exported Excel-compatible files to {directory}");
        }

        public async Task<string> ExportToZip(List<ScanSession> sessions, string zipFilePath)
        {
            // This would require System.IO.Compression
            // For now, create a temporary directory structure
            
            var tempDir = Path.Combine(Path.GetTempPath(), $"AlbionExport_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                await ExportSessionsToCsv(sessions, Path.Combine(tempDir, "sessions.csv"));
                await ExportSessionsToJson(sessions, Path.Combine(tempDir, "sessions.json"));
                await ExportEntityDetectionsToCsv(sessions, Path.Combine(tempDir, "detections.csv"));
                await ExportAvalonianResultsToCsv(sessions, Path.Combine(tempDir, "avalonian.csv"));
                
                // In a real implementation, you would create a ZIP file here
                _logger?.LogInformation($"Export data prepared in {tempDir}");
                
                return tempDir;
            }
            catch
            {
                Directory.Delete