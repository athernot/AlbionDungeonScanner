using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Analytics;
using Newtonsoft.Json;
using System.Data;

namespace AlbionDungeonScanner.Core.Services
{
    public class DataPersistenceService
    {
        private readonly ILogger<DataPersistenceService> _logger;
        private readonly string _connectionString;
        private readonly string _dataDirectory;

        public DataPersistenceService(ILogger<DataPersistenceService> logger = null)
        {
            _logger = logger;
            _dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            
            Directory.CreateDirectory(_dataDirectory);
            
            var dbPath = Path.Combine(_dataDirectory, "albion_scanner.db");
            _connectionString = $"Data Source={dbPath}";
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Create tables
            var createTablesScript = @"
                CREATE TABLE IF NOT EXISTS ScanSessions (
                    SessionId TEXT PRIMARY KEY,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    Duration TEXT,
                    TotalEntities INTEGER DEFAULT 0,
                    AvalonianEntities INTEGER DEFAULT 0,
                    EstimatedValue INTEGER DEFAULT 0,
                    Efficiency REAL DEFAULT 0.0,
                    Data TEXT
                );

                CREATE TABLE IF NOT EXISTS EntityDetections (
                    DetectionId TEXT PRIMARY KEY,
                    SessionId TEXT NOT NULL,
                    EntityId TEXT NOT NULL,
                    EntityName TEXT NOT NULL,
                    EntityType TEXT NOT NULL,
                    DungeonType TEXT NOT NULL,
                    PositionX REAL NOT NULL,
                    PositionY REAL NOT NULL,
                    PositionZ REAL NOT NULL,
                    DetectedAt TEXT NOT NULL,
                    Data TEXT,
                    FOREIGN KEY (SessionId) REFERENCES ScanSessions (SessionId)
                );

                CREATE TABLE IF NOT EXISTS AvalonianResults (
                    ResultId TEXT PRIMARY KEY,
                    SessionId TEXT NOT NULL,
                    EntityName TEXT NOT NULL,
                    EntityType TEXT NOT NULL,
                    Tier INTEGER NOT NULL,
                    EstimatedValue INTEGER NOT NULL,
                    Fame INTEGER NOT NULL,
                    ThreatLevel TEXT NOT NULL,
                    Priority TEXT NOT NULL,
                    PositionX REAL NOT NULL,
                    PositionY REAL NOT NULL,
                    PositionZ REAL NOT NULL,
                    RecommendedStrategy TEXT,
                    Data TEXT,
                    FOREIGN KEY (SessionId) REFERENCES ScanSessions (SessionId)
                );

                CREATE TABLE IF NOT EXISTS Analytics (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DungeonType TEXT NOT NULL,
                    AnalyticsData TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Configurations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ConfigKey TEXT NOT NULL UNIQUE,
                    ConfigValue TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS PerformanceMetrics (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    CpuUsage REAL NOT NULL,
                    MemoryUsage INTEGER NOT NULL,
                    PacketsPerSecond REAL NOT NULL,
                    EntitiesPerSecond REAL NOT NULL,
                    SessionId TEXT,
                    FOREIGN KEY (SessionId) REFERENCES ScanSessions (SessionId)
                );

                CREATE INDEX IF NOT EXISTS idx_sessions_start_time ON ScanSessions(StartTime);
                CREATE INDEX IF NOT EXISTS idx_detections_session ON EntityDetections(SessionId);
                CREATE INDEX IF NOT EXISTS idx_detections_type ON EntityDetections(EntityType);
                CREATE INDEX IF NOT EXISTS idx_avalonian_session ON AvalonianResults(SessionId);
                CREATE INDEX IF NOT EXISTS idx_avalonian_value ON AvalonianResults(EstimatedValue);
                CREATE INDEX IF NOT EXISTS idx_performance_timestamp ON PerformanceMetrics(Timestamp);
                CREATE INDEX IF NOT EXISTS idx_analytics_dungeon_type ON Analytics(DungeonType);
            ";

            using var command = new SqliteCommand(createTablesScript, connection);
            command.ExecuteNonQuery();

            _logger?.LogInformation("Database initialized successfully");
        }

        public async Task SaveSessionAsync(ScanSession session)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Save session
                var sessionSql = @"
                    INSERT OR REPLACE INTO ScanSessions 
                    (SessionId, StartTime, EndTime, Duration, TotalEntities, AvalonianEntities, EstimatedValue, Efficiency, Data)
                    VALUES (@SessionId, @StartTime, @EndTime, @Duration, @TotalEntities, @AvalonianEntities, @EstimatedValue, @Efficiency, @Data)";

                using var sessionCmd = new SqliteCommand(sessionSql, connection, transaction);
                sessionCmd.Parameters.AddWithValue("@SessionId", session.SessionId.ToString());
                sessionCmd.Parameters.AddWithValue("@StartTime", session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sessionCmd.Parameters.AddWithValue("@EndTime", session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
                sessionCmd.Parameters.AddWithValue("@Duration", session.Duration.ToString());
                sessionCmd.Parameters.AddWithValue("@TotalEntities", session.Statistics?.TotalEntities ?? 0);
                sessionCmd.Parameters.AddWithValue("@AvalonianEntities", session.Statistics?.AvalonianEntities ?? 0);
                sessionCmd.Parameters.AddWithValue("@EstimatedValue", session.Statistics?.EstimatedTotalValue ?? 0);
                sessionCmd.Parameters.AddWithValue("@Efficiency", session.Statistics?.DungeonEfficiency ?? 0.0);
                sessionCmd.Parameters.AddWithValue("@Data", JsonConvert.SerializeObject(session));

                await sessionCmd.ExecuteNonQueryAsync();

                // Save entity detections
                foreach (var detection in session.DetectedEntities)
                {
                    await SaveEntityDetectionAsync(detection, connection, transaction);
                }

                // Save Avalonian results
                foreach (var result in session.AvalonianResults)
                {
                    await SaveAvalonianResultAsync(result, session.SessionId, connection, transaction);
                }

                transaction.Commit();
                _logger?.LogDebug($"Saved session {session.SessionId} to database");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger?.LogError(ex, $"Failed to save session {session.SessionId}");
                throw;
            }
        }

        private async Task SaveEntityDetectionAsync(EntityDetection detection, SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                INSERT OR REPLACE INTO EntityDetections 
                (DetectionId, SessionId, EntityId, EntityName, EntityType, DungeonType, PositionX, PositionY, PositionZ, DetectedAt, Data)
                VALUES (@DetectionId, @SessionId, @EntityId, @EntityName, @EntityType, @DungeonType, @PositionX, @PositionY, @PositionZ, @DetectedAt, @Data)";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@DetectionId", detection.DetectionId.ToString());
            cmd.Parameters.AddWithValue("@SessionId", detection.SessionId.ToString());
            cmd.Parameters.AddWithValue("@EntityId", detection.EntityId);
            cmd.Parameters.AddWithValue("@EntityName", detection.EntityName);
            cmd.Parameters.AddWithValue("@EntityType", detection.EntityType.ToString());
            cmd.Parameters.AddWithValue("@DungeonType", detection.DungeonType.ToString());
            cmd.Parameters.AddWithValue("@PositionX", detection.Position.X);
            cmd.Parameters.AddWithValue("@PositionY", detection.Position.Y);
            cmd.Parameters.AddWithValue("@PositionZ", detection.Position.Z);
            cmd.Parameters.AddWithValue("@DetectedAt", detection.DetectedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@Data", JsonConvert.SerializeObject(detection));

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task SaveAvalonianResultAsync(AvalonianScanResult result, Guid sessionId, SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                INSERT OR REPLACE INTO AvalonianResults 
                (ResultId, SessionId, EntityName, EntityType, Tier, EstimatedValue, Fame, ThreatLevel, Priority, PositionX, PositionY, PositionZ, RecommendedStrategy, Data)
                VALUES (@ResultId, @SessionId, @EntityName, @EntityType, @Tier, @EstimatedValue, @Fame, @ThreatLevel, @Priority, @PositionX, @PositionY, @PositionZ, @RecommendedStrategy, @Data)";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@ResultId", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@SessionId", sessionId.ToString());
            cmd.Parameters.AddWithValue("@EntityName", result.EntityData.Name);
            cmd.Parameters.AddWithValue("@EntityType", result.EntityData.Type.ToString());
            cmd.Parameters.AddWithValue("@Tier", result.EntityData.Tier);
            cmd.Parameters.AddWithValue("@EstimatedValue", result.EstimatedLoot.MaxSilver);
            cmd.Parameters.AddWithValue("@Fame", result.EstimatedLoot.Fame);
            cmd.Parameters.AddWithValue("@ThreatLevel", result.ThreatLevel.ToString());
            cmd.Parameters.AddWithValue("@Priority", result.EntityData.Priority.ToString());
            cmd.Parameters.AddWithValue("@PositionX", result.Position.X);
            cmd.Parameters.AddWithValue("@PositionY", result.Position.Y);
            cmd.Parameters.AddWithValue("@PositionZ", result.Position.Z);
            cmd.Parameters.AddWithValue("@RecommendedStrategy", result.RecommendedStrategy ?? "");
            cmd.Parameters.AddWithValue("@Data", JsonConvert.SerializeObject(result));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SaveAnalyticsAsync(Dictionary<DungeonType, DungeonAnalytics> analytics)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var kvp in analytics)
            {
                var sql = @"
                    INSERT OR REPLACE INTO Analytics (DungeonType, AnalyticsData, UpdatedAt)
                    VALUES (@DungeonType, @AnalyticsData, @UpdatedAt)";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@DungeonType", kvp.Key.ToString());
                cmd.Parameters.AddWithValue("@AnalyticsData", JsonConvert.SerializeObject(kvp.Value));
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task SavePerformanceMetricAsync(PerformanceMetrics metrics, Guid? sessionId = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT INTO PerformanceMetrics (Timestamp, CpuUsage, MemoryUsage, PacketsPerSecond, EntitiesPerSecond, SessionId)
                VALUES (@Timestamp, @CpuUsage, @MemoryUsage, @PacketsPerSecond, @EntitiesPerSecond, @SessionId)";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Timestamp", metrics.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@CpuUsage", metrics.CpuUsage);
            cmd.Parameters.AddWithValue("@MemoryUsage", metrics.MemoryUsage);
            cmd.Parameters.AddWithValue("@PacketsPerSecond", metrics.PacketsPerSecond);
            cmd.Parameters.AddWithValue("@EntitiesPerSecond", metrics.EntitiesDetectedPerSecond);
            cmd.Parameters.AddWithValue("@SessionId", sessionId?.ToString() ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<ScanSession>> GetSessionsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT Data FROM ScanSessions WHERE 1=1";
            var parameters = new List<SqliteParameter>();

            if (startDate.HasValue)
            {
                sql += " AND StartTime >= @StartDate";
                parameters.Add(new SqliteParameter("@StartDate", startDate.Value.ToString("yyyy-MM-dd HH:mm:ss")));
            }

            if (endDate.HasValue)
            {
                sql += " AND StartTime <= @EndDate";
                parameters.Add(new SqliteParameter("@EndDate", endDate.Value.ToString("yyyy-MM-dd HH:mm:ss")));
            }

            sql += " ORDER BY StartTime DESC";

            using var cmd = new SqliteCommand(sql, connection);
            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            var sessions = new List<ScanSession>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var sessionData = reader.GetString("Data");
                var session = JsonConvert.DeserializeObject<ScanSession>(sessionData);
                sessions.Add(session);
            }

            return sessions;
        }

        public async Task<Dictionary<DungeonType, DungeonAnalytics>> GetAnalyticsAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT DungeonType, AnalyticsData FROM Analytics ORDER BY UpdatedAt DESC";
            using var cmd = new SqliteCommand(sql, connection);

            var analytics = new Dictionary<DungeonType, DungeonAnalytics>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var dungeonTypeStr = reader.GetString("DungeonType");
                var analyticsData = reader.GetString("AnalyticsData");
                
                if (Enum.TryParse<DungeonType>(dungeonTypeStr, out var dungeonType))
                {
                    var data = JsonConvert.DeserializeObject<DungeonAnalytics>(analyticsData);
                    analytics[dungeonType] = data;
                }
            }

            return analytics;
        }

        public async Task<List<PerformanceMetrics>> GetPerformanceMetricsAsync(DateTime? startDate = null, int? limitCount = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT Timestamp, CpuUsage, MemoryUsage, PacketsPerSecond, EntitiesPerSecond FROM PerformanceMetrics WHERE 1=1";
            var parameters = new List<SqliteParameter>();

            if (startDate.HasValue)
            {
                sql += " AND Timestamp >= @StartDate";
                parameters.Add(new SqliteParameter("@StartDate", startDate.Value.ToString("yyyy-MM-dd HH:mm:ss")));
            }

            sql += " ORDER BY Timestamp DESC";

            if (limitCount.HasValue)
            {
                sql += $" LIMIT {limitCount.Value}";
            }

            using var cmd = new SqliteCommand(sql, connection);
            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            var metrics = new List<PerformanceMetrics>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                metrics.Add(new PerformanceMetrics
                {
                    Timestamp = DateTime.Parse(reader.GetString("Timestamp")),
                    CpuUsage = reader.GetDouble("CpuUsage"),
                    MemoryUsage = reader.GetInt64("MemoryUsage"),
                    PacketsPerSecond = reader.GetDouble("PacketsPerSecond"),
                    EntitiesDetectedPerSecond = reader.GetDouble("EntitiesPerSecond")
                });
            }

            return metrics;
        }

        public async Task CleanupOldDataAsync(TimeSpan retentionPeriod)
        {
            var cutoffDate = DateTime.UtcNow - retentionPeriod;
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Delete old sessions and their related data
                var deleteSql = @"
                    DELETE FROM AvalonianResults WHERE SessionId IN (
                        SELECT SessionId FROM ScanSessions WHERE StartTime < @CutoffDate
                    );
                    DELETE FROM EntityDetections WHERE SessionId IN (
                        SELECT SessionId FROM ScanSessions WHERE StartTime < @CutoffDate
                    );
                    DELETE FROM PerformanceMetrics WHERE SessionId IN (
                        SELECT SessionId FROM ScanSessions WHERE StartTime < @CutoffDate
                    );
                    DELETE FROM ScanSessions WHERE StartTime < @CutoffDate;
                    DELETE FROM PerformanceMetrics WHERE Timestamp < @CutoffDate;
                ";

                using var cmd = new SqliteCommand(deleteSql, connection, transaction);
                cmd.Parameters.AddWithValue("@CutoffDate", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));

                var deletedRows = await cmd.ExecuteNonQueryAsync();
                transaction.Commit();

                _logger?.LogInformation($"Cleaned up old data, cutoff date: {cutoffDate:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger?.LogError(ex, "Failed to cleanup old data");
                throw;
            }
        }

        public async Task<bool> BackupDatabaseAsync(string backupPath)
        {
            try
            {
                var sourceDb = Path.Combine(_dataDirectory, "albion_scanner.db");
                var backupFile = Path.Combine(backupPath, $"albion_scanner_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile));
                File.Copy(sourceDb, backupFile, true);
                
                _logger?.LogInformation($"Database backed up to: {backupFile}");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to backup database");
                return false;
            }
        }

        public async Task<Dictionary<string, object>> GetDatabaseStatistics()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var stats = new Dictionary<string, object>();

            // Get table row counts
            var tables = new[] { "ScanSessions", "EntityDetections", "AvalonianResults", "PerformanceMetrics", "Analytics" };
            
            foreach (var table in tables)
            {
                using var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {table}", connection);
                stats[$"{table}Count"] = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            // Get database file size
            var dbPath = Path.Combine(_dataDirectory, "albion_scanner.db");
            if (File.Exists(dbPath))
            {
                stats["DatabaseSizeMB"] = new FileInfo(dbPath).Length / 1024.0 / 1024.0;
            }

            // Get oldest and newest session dates
            using var dateCmd = new SqliteCommand("SELECT MIN(StartTime), MAX(StartTime) FROM ScanSessions", connection);
            using var dateReader = await dateCmd.ExecuteReaderAsync();
            if (await dateReader.ReadAsync())
            {
                if (!dateReader.IsDBNull(0))
                    stats["OldestSession"] = DateTime.Parse(dateReader.GetString(0));
                if (!dateReader.IsDBNull(1))
                    stats["NewestSession"] = DateTime.Parse(dateReader.GetString(1));
            }

            return stats;
        }

        public void Dispose()
        {
            // SQLite connections are automatically closed when disposed
        }
    }
}