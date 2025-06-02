using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Configuration;
using Newtonsoft.Json;

namespace AlbionDungeonScanner.Core.Services
{
    public class NotificationManager
    {
        private readonly ILogger<NotificationManager> _logger;
        private readonly ConfigurationManager _configurationManager;
        private readonly HttpClient _httpClient;
        private readonly List<INotificationProvider> _providers;

        public NotificationManager(
            ILogger<NotificationManager> logger = null, 
            ConfigurationManager configurationManager = null)
        {
            _logger = logger;
            _configurationManager = configurationManager;
            _httpClient = new HttpClient();
            _providers = new List<INotificationProvider>();
            
            InitializeProviders();
        }

        private void InitializeProviders()
        {
            // Discord Webhook Provider
            var discordConfig = _configurationManager?.Current?.Notifications?.Discord;
            if (discordConfig?.Enabled == true && !string.IsNullOrEmpty(discordConfig.WebhookUrl))
            {
                _providers.Add(new DiscordNotificationProvider(_httpClient, discordConfig, _logger));
            }

            // Desktop Notification Provider
            _providers.Add(new DesktopNotificationProvider(_logger));

            // Email Provider (if configured)
            var emailConfig = _configurationManager?.Current?.Notifications?.Email;
            if (emailConfig?.Enabled == true && !string.IsNullOrEmpty(emailConfig.SmtpServer))
            {
                _providers.Add(new EmailNotificationProvider(emailConfig, _logger));
            }

            // Sound Alert Provider
            _providers.Add(new SoundAlertProvider(_logger));

            _logger?.LogInformation($"Initialized {_providers.Count} notification providers");
        }

        public async Task SendHighValueAlert(AvalonianScanResult result)
        {
            var notification = new NotificationData
            {
                Type = NotificationType.HighValueTarget,
                Title = "💰 High Value Target Detected!",
                Message = $"{result.EntityData.Name} detected with estimated value of {result.EstimatedLoot.MaxSilver:N0} silver",
                Priority = GetNotificationPriority(result.EntityData.Priority),
                Data = result,
                Timestamp = DateTime.UtcNow,
                AdditionalInfo = new Dictionary<string, object>
                {
                    ["Tier"] = result.EntityData.Tier,
                    ["Position"] = $"({result.Position.X:F0}, {result.Position.Z:F0})",
                    ["ThreatLevel"] = result.ThreatLevel.ToString(),
                    ["Strategy"] = result.RecommendedStrategy
                }
            };

            await SendNotification(notification);
        }

        public async Task SendBossAlert(AvalonianScanResult bossResult)
        {
            var notification = new NotificationData
            {
                Type = NotificationType.Boss,
                Title = "👹 Boss Detected!",
                Message = $"{bossResult.EntityData.Name} boss detected! Threat Level: {bossResult.ThreatLevel}",
                Priority = NotificationPriority.High,
                Data = bossResult,
                Timestamp = DateTime.UtcNow,
                AdditionalInfo = new Dictionary<string, object>
                {
                    ["Strategy"] = bossResult.RecommendedStrategy,
                    ["Abilities"] = bossResult.EntityData.Abilities,
                    ["EstimatedValue"] = bossResult.EstimatedLoot.MaxSilver,
                    ["Fame"] = bossResult.EstimatedLoot.Fame,
                    ["Position"] = $"({bossResult.Position.X:F0}, {bossResult.Position.Z:F0})"
                }
            };

            await SendNotification(notification);
        }

        public async Task SendSessionSummary(ScanSession session)
        {
            var notification = new NotificationData
            {
                Type = NotificationType.SessionComplete,
                Title = "📊 Scan Session Complete",
                Message = $"Session completed: {session.Statistics.TotalEntities} entities detected, {session.Statistics.EstimatedTotalValue:N0} silver value",
                Priority = NotificationPriority.Normal,
                Data = session.Statistics,
                Timestamp = DateTime.UtcNow,
                AdditionalInfo = new Dictionary<string, object>
                {
                    ["Duration"] = session.Duration.ToString(@"hh\:mm\:ss"),
                    ["AvalonianEntities"] = session.Statistics.AvalonianEntities,
                    ["Efficiency"] = session.Statistics.DungeonEfficiency.ToString("F2"),
                    ["Fame"] = session.Statistics.EstimatedFame
                }
            };

            await SendNotification(notification);
        }

        public async Task SendEfficiencyAlert(double currentEfficiency, double averageEfficiency)
        {
            if (currentEfficiency < averageEfficiency * 0.7) // 30% below average
            {
                var notification = new NotificationData
                {
                    Type = NotificationType.LowEfficiency,
                    Title = "⚠️ Low Efficiency Alert",
                    Message = $"Current efficiency ({currentEfficiency:F2}) is significantly below average ({averageEfficiency:F2})",
                    Priority = NotificationPriority.Low,
                    Timestamp = DateTime.UtcNow,
                    AdditionalInfo = new Dictionary<string, object>
                    {
                        ["CurrentEfficiency"] = currentEfficiency,
                        ["AverageEfficiency"] = averageEfficiency,
                        ["PerformanceDrop"] = $"{((averageEfficiency - currentEfficiency) / averageEfficiency * 100):F1}%"
                    }
                };

                await SendNotification(notification);
            }
        }

        public async Task SendSystemAlert(string title, string message, NotificationPriority priority = NotificationPriority.Normal)
        {
            var notification = new NotificationData
            {
                Type = NotificationType.SystemAlert,
                Title = $"🔧 {title}",
                Message = message,
                Priority = priority,
                Timestamp = DateTime.UtcNow
            };

            await SendNotification(notification);
        }

        public async Task SendCustomNotification(NotificationData notification)
        {
            await SendNotification(notification);
        }

        private async Task SendNotification(NotificationData notification)
        {
            // Check if this notification type is enabled
            if (!IsNotificationTypeEnabled(notification.Type))
                return;

            // Check priority threshold
            var minPriority = _configurationManager?.Current?.Notifications?.MinimumPriority ?? NotificationPriority.Normal;
            if (notification.Priority < minPriority)
                return;

            var tasks = _providers.Select(provider => 
                SendNotificationSafely(provider, notification));
            
            await Task.WhenAll(tasks);

            _logger?.LogDebug($"Sent notification: {notification.Title} to {_providers.Count} providers");
        }

        private async Task SendNotificationSafely(INotificationProvider provider, NotificationData notification)
        {
            try
            {
                if (provider.SupportsNotificationType(notification.Type) && 
                    provider.ShouldSendNotification(notification.Priority))
                {
                    await provider.SendNotificationAsync(notification);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to send notification via {provider.GetType().Name}");
            }
        }

        private bool IsNotificationTypeEnabled(NotificationType type)
        {
            var typeSettings = _configurationManager?.Current?.Notifications?.TypeSettings;
            if (typeSettings == null)
                return true;

            return typeSettings.TryGetValue(type, out var enabled) ? enabled : true;
        }

        private NotificationPriority GetNotificationPriority(ScanPriority scanPriority)
        {
            return scanPriority switch
            {
                ScanPriority.Critical => NotificationPriority.Critical,
                ScanPriority.High => NotificationPriority.High,
                ScanPriority.Medium => NotificationPriority.Normal,
                _ => NotificationPriority.Low
            };
        }

        public void RefreshProviders()
        {
            _providers.Clear();
            InitializeProviders();
            _logger?.LogInformation("Notification providers refreshed");
        }

        public List<string> GetActiveProviders()
        {
            return _providers.ConvertAll(p => p.GetType().Name);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            foreach (var provider in _providers)
            {
                if (provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    // Notification Provider Interfaces and Implementations
    public interface INotificationProvider
    {
        Task SendNotificationAsync(NotificationData notification);
        bool SupportsNotificationType(NotificationType type);
        bool ShouldSendNotification(NotificationPriority priority);
    }

    public class DiscordNotificationProvider : INotificationProvider
    {
        private readonly HttpClient _httpClient;
        private readonly DiscordNotificationConfig _config;
        private readonly ILogger _logger;

        public DiscordNotificationProvider(HttpClient httpClient, DiscordNotificationConfig config, ILogger logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task SendNotificationAsync(NotificationData notification)
        {
            var embed = new
            {
                title = notification.Title,
                description = notification.Message,
                color = GetColorForPriority(notification.Priority),
                timestamp = notification.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                fields = BuildEmbedFields(notification),
                footer = new { text = "Albion Dungeon Scanner", icon_url = _config.AvatarUrl }
            };

            var payload = new 
            { 
                username = _config.Username,
                avatar_url = _config.AvatarUrl,
                embeds = new[] { embed } 
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_config.WebhookUrl, content);
            response.EnsureSuccessStatusCode();
        }

        public bool SupportsNotificationType(NotificationType type) => true;

        public bool ShouldSendNotification(NotificationPriority priority)
        {
            return priority >= _config.MinPriority;
        }

        private int GetColorForPriority(NotificationPriority priority) => priority switch
        {
            NotificationPriority.Critical => 0xFF0000, // Red
            NotificationPriority.High => 0xFF8000,     // Orange
            NotificationPriority.Normal => 0x00FF00,   // Green
            _ => 0x808080                              // Gray
        };

        private object[] BuildEmbedFields(NotificationData notification)
        {
            var fields = new List<object>();

            if (notification.AdditionalInfo != null)
            {
                foreach (var kvp in notification.AdditionalInfo.Take(25)) // Discord limit
                {
                    fields.Add(new { name = kvp.Key, value = kvp.Value.ToString(), inline = true });
                }
            }

            return fields.ToArray();
        }
    }

    public class DesktopNotificationProvider : INotificationProvider
    {
        private readonly ILogger _logger;

        public DesktopNotificationProvider(ILogger logger)
        {
            _logger = logger;
        }

        public Task SendNotificationAsync(NotificationData notification)
        {
            try
            {
                // For Windows, we can use toast notifications
                // This is a simplified implementation
                _logger?.LogInformation($"Desktop Notification: {notification.Title} - {notification.Message}");
                
                // In a real implementation, you would use:
                // - Windows.UI.Notifications for Windows 10/11
                // - Plyer for cross-platform notifications
                // - Or System.Windows.Forms.NotifyIcon for legacy support
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send desktop notification");
                return Task.CompletedTask;
            }
        }

        public bool SupportsNotificationType(NotificationType type) => true;
        public bool ShouldSendNotification(NotificationPriority priority) => priority >= NotificationPriority.Normal;
    }

    public class EmailNotificationProvider : INotificationProvider
    {
        private readonly EmailNotificationConfig _config;
        private readonly ILogger _logger;

        public EmailNotificationProvider(EmailNotificationConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendNotificationAsync(NotificationData notification)
        {
            try
            {
                // Email implementation would use System.Net.Mail.SmtpClient
                // This is a placeholder implementation
                _logger?.LogInformation($"Email Notification: {notification.Title} - {notification.Message}");
                
                // In a real implementation:
                // var client = new SmtpClient(_config.SmtpServer, _config.SmtpPort);
                // client.EnableSsl = _config.UseSSL;
                // client.Credentials = new NetworkCredential(_config.Username, _config.Password);
                // await client.SendMailAsync(mailMessage);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send email notification");
            }
        }

        public bool SupportsNotificationType(NotificationType type) => 
            type == NotificationType.HighValueTarget || 
            type == NotificationType.SessionComplete ||
            type == NotificationType.SystemAlert;

        public bool ShouldSendNotification(NotificationPriority priority) => priority >= NotificationPriority.High;
    }

    public class SoundAlertProvider : INotificationProvider
    {
        private readonly ILogger _logger;

        public SoundAlertProvider(ILogger logger)
        {
            _logger = logger;
        }

        public Task SendNotificationAsync(NotificationData notification)
        {
            try
            {
                // Play different sounds based on notification type and priority
                switch (notification.Priority)
                {
                    case NotificationPriority.Critical:
                        PlaySound("critical");
                        break;
                    case NotificationPriority.High:
                        PlaySound("high");
                        break;
                    case NotificationPriority.Normal:
                        PlaySound("normal");
                        break;
                    default:
                        PlaySound("low");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to play sound alert");
            }
            
            return Task.CompletedTask;
        }

        private void PlaySound(string priority)
        {
            try
            {
                // Simple system sound implementation
                switch (priority)
                {
                    case "critical":
                        System.Media.SystemSounds.Hand.Play();
                        break;
                    case "high":
                        System.Media.SystemSounds.Exclamation.Play();
                        break;
                    case "normal":
                        System.Media.SystemSounds.Asterisk.Play();
                        break;
                    default:
                        System.Media.SystemSounds.Beep.Play();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"Failed to play system sound: {ex.Message}");
            }
        }

        public bool SupportsNotificationType(NotificationType type) => 
            type == NotificationType.HighValueTarget || 
            type == NotificationType.Boss ||
            type == NotificationType.SystemAlert;

        public bool ShouldSendNotification(NotificationPriority priority) => priority >= NotificationPriority.High;
    }
}