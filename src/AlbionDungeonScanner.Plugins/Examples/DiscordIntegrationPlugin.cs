using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using AlbionDungeonScanner.Core.Plugins;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Avalonian;

namespace AlbionDungeonScanner.Plugins.Discord
{
    /// <summary>
    /// Discord Integration Plugin for Albion Dungeon Scanner
    /// Sends notifications to Discord webhooks when high-value entities are detected
    /// </summary>
    public class DiscordIntegrationPlugin : IPlugin
    {
        private ILogger<DiscordIntegrationPlugin> _logger;
        private IPluginContext _context;
        private HttpClient _httpClient;
        private DiscordPluginSettings _settings;
        private DateTime _lastNotificationTime = DateTime.MinValue;

        public string Name => "Discord Integration";
        public string Version => "1.0.0";
        public string Author => "Albion Scanner Team";
        public string Description => "Send dungeon scanner notifications to Discord channels via webhooks";
        public bool IsEnabled { get; set; }

        public async Task<bool> InitializeAsync(IServiceProvider serviceProvider, IPluginContext context)
        {
            try
            {
                _logger = serviceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<DiscordIntegrationPlugin>();
                _context = context;
                _httpClient = serviceProvider.GetRequiredService<HttpClient>();
                
                // Subscribe to scanner events
                _context.EntityDetected += OnEntityDetected;
                _context.AvalonianEntityDetected += OnAvalonianEntityDetected;
                _context.DungeonCompleted += OnDungeonCompleted;
                
                _logger.LogInformation("Discord Integration Plugin initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize Discord Integration Plugin");
                return false;
            }
        }

        public async Task ShutdownAsync()
        {
            try
            {
                // Unsubscribe from events
                if (_context != null)
                {
                    _context.EntityDetected -= OnEntityDetected;
                    _context.AvalonianEntityDetected -= OnAvalonianEntityDetected;
                    _context.DungeonCompleted -= OnDungeonCompleted;
                }
                
                // Send shutdown notification if enabled
                if (_settings?.SendShutdownNotifications == true && !string.IsNullOrEmpty(_settings.WebhookUrl))
                {
                    await SendDiscordMessage("🔴 Albion Dungeon Scanner offline", "Scanner has been stopped.");
                }
                
                _httpClient?.Dispose();
                _logger?.LogInformation("Discord Integration Plugin shut down successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during Discord plugin shutdown");
            }
        }

        public async Task<PluginConfigurationResult> ConfigureAsync(Dictionary<string, object> settings)
        {
            try
            {
                _settings = new DiscordPluginSettings();
                
                if (settings.ContainsKey("WebhookUrl"))
                    _settings.WebhookUrl = settings["WebhookUrl"].ToString();
                
                if (settings.ContainsKey("ChannelName"))
                    _settings.ChannelName = settings["ChannelName"].ToString();
                
                if (settings.ContainsKey("MinimumValueThreshold"))
                    _settings.MinimumValueThreshold = Convert.ToInt32(settings["MinimumValueThreshold"]);
                
                if (settings.ContainsKey("NotifyBosses"))
                    _settings.NotifyBosses = Convert.ToBoolean(settings["NotifyBosses"]);
                
                if (settings.ContainsKey("NotifyAvalonianOnly"))
                    _settings.NotifyAvalonianOnly = Convert.ToBoolean(settings["NotifyAvalonianOnly"]);
                
                if (settings.ContainsKey("NotificationCooldownSeconds"))
                    _settings.NotificationCooldownSeconds = Convert.ToInt32(settings["NotificationCooldownSeconds"]);
                
                if (settings.ContainsKey("SendStartupNotifications"))
                    _settings.SendStartupNotifications = Convert.ToBoolean(settings["SendStartupNotifications"]);
                
                if (settings.ContainsKey("SendShutdownNotifications"))
                    _settings.SendShutdownNotifications = Convert.ToBoolean(settings["SendShutdownNotifications"]);
                
                if (settings.ContainsKey("IncludeDetailedStats"))
                    _settings.IncludeDetailedStats = Convert.ToBoolean(settings["IncludeDetailedStats"]);

                // Validate webhook URL
                if (!string.IsNullOrEmpty(_settings.WebhookUrl))
                {
                    var isValid = await ValidateWebhookAsync(_settings.WebhookUrl);
                    if (!isValid)
                    {
                        return new PluginConfigurationResult
                        {
                            Success = false,
                            ErrorMessage = "Invalid Discord webhook URL or webhook is not accessible"
                        };
                    }
                }

                // Send startup notification if enabled
                if (_settings.SendStartupNotifications && !string.IsNullOrEmpty(_settings.WebhookUrl))
                {
                    await SendDiscordMessage("🟢 Albion Dungeon Scanner online", 
                        "Scanner has been started and is monitoring for dungeon entities.");
                }

                _logger.LogInformation("Discord plugin configured successfully");
                
                return new PluginConfigurationResult
                {
                    Success = true,
                    UpdatedSettings = ConvertSettingsToDict(_settings)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring Discord plugin");
                return new PluginConfigurationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async void OnEntityDetected(object sender, EntityDetectedEventArgs e)
        {
            try
            {
                if (!ShouldNotify(e.Entity))
                    return;

                var embed = CreateEntityEmbed(e.Entity);
                await SendDiscordEmbed(embed);
                
                _lastNotificationTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending entity detection notification to Discord");
            }
        }

        private async void OnAvalonianEntityDetected(object sender, AvalonianEntityEventArgs e)
        {
            try
            {
                if (!ShouldNotifyAvalonian(e.Result))
                    return;

                var embed = CreateAvalonianEmbed(e.Result);
                await SendDiscordEmbed(embed);
                
                _lastNotificationTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Avalonian detection notification to Discord");
            }
        }

        private async void OnDungeonCompleted(object sender, DungeonCompletedEventArgs e)
        {
            try
            {
                if (!_settings.IncludeDetailedStats)
                    return;

                var embed = CreateDungeonCompletedEmbed(e);
                await SendDiscordEmbed(embed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending dungeon completion notification to Discord");
            }
        }

        private bool ShouldNotify(DungeonEntity entity)
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.WebhookUrl))
                return false;

            // Check cooldown
            if ((DateTime.Now - _lastNotificationTime).TotalSeconds < _settings.NotificationCooldownSeconds)
                return false;

            // Check if only Avalonian entities should be notified
            if (_settings.NotifyAvalonianOnly && entity.DungeonType != DungeonType.Avalonian)
                return false;

            // Check if bosses should be notified
            if (entity.Type == EntityType.Boss && _settings.NotifyBosses)
                return true;

            // Check value threshold for chests
            if (entity.Type == EntityType.Chest)
            {
                var estimatedValue = EstimateEntityValue(entity);
                return estimatedValue >= _settings.MinimumValueThreshold;
            }

            return false;
        }

        private bool ShouldNotifyAvalonian(AvalonianScanResult result)
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.WebhookUrl))
                return false;

            // Check cooldown
            if ((DateTime.Now - _lastNotificationTime).TotalSeconds < _settings.NotificationCooldownSeconds)
                return false;

            // Check priority level
            return result.EntityData.Priority >= ScanPriority.High;
        }

        private int EstimateEntityValue(DungeonEntity entity)
        {
            // Simplified value estimation - could be enhanced with real data
            var baseValue = entity.Type switch
            {
                EntityType.Chest when entity.DungeonType == DungeonType.Avalonian => 15000,
                EntityType.Chest => 5000,
                EntityType.Boss when entity.DungeonType == DungeonType.Avalonian => 50000,
                EntityType.Boss => 20000,
                _ => 1000
            };

            return baseValue;
        }

        private DiscordEmbed CreateEntityEmbed(DungeonEntity entity)
        {
            var color = entity.Type switch
            {
                EntityType.Chest when entity.DungeonType == DungeonType.Avalonian => 0xFFD700, // Gold
                EntityType.Chest => 0x00FF00, // Green
                EntityType.Boss => 0xFF0000, // Red
                EntityType.Mob => 0xFFA500, // Orange
                _ => 0x808080 // Gray
            };

            var emoji = entity.Type switch
            {
                EntityType.Chest => "📦",
                EntityType.Boss => "👹",
                EntityType.Mob => "⚔️",
                EntityType.ResourceNode => "🪨",
                EntityType.Portal => "🌀",
                _ => "❓"
            };

            var dungeonTypeText = entity.DungeonType == DungeonType.Avalonian ? " (Avalonian)" : "";
            var estimatedValue = EstimateEntityValue(entity);

            return new DiscordEmbed
            {
                Title = $"{emoji} {entity.Type} Detected{dungeonTypeText}",
                Description = $"**{entity.Name}**",
                Color = color,
                Fields = new List<DiscordEmbedField>
                {
                    new DiscordEmbedField
                    {
                        Name = "Location",
                        Value = $"({entity.Position.X:F1}, {entity.Position.Z:F1})",
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Estimated Value",
                        Value = $"{estimatedValue:N0} silver",
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Detected",
                        Value = entity.LastSeen.ToString("HH:mm:ss"),
                        Inline = true
                    }
                },
                Timestamp = entity.LastSeen,
                Footer = new DiscordEmbedFooter
                {
                    Text = "Albion Dungeon Scanner"
                }
            };
        }

        private DiscordEmbed CreateAvalonianEmbed(AvalonianScanResult result)
        {
            var priorityEmoji = result.EntityData.Priority switch
            {
                ScanPriority.Critical => "🔥",
                ScanPriority.High => "⭐",
                ScanPriority.Medium => "📌",
                _ => "ℹ️"
            };

            return new DiscordEmbed
            {
                Title = $"{priorityEmoji} Avalonian {result.EntityData.Type} Detected",
                Description = $"**{result.EntityData.Name}**",
                Color = 0xFFD700, // Gold
                Fields = new List<DiscordEmbedField>
                {
                    new DiscordEmbedField
                    {
                        Name = "Tier",
                        Value = $"T{result.EntityData.Tier}",
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Priority",
                        Value = result.EntityData.Priority.ToString(),
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Estimated Loot",
                        Value = $"{result.EstimatedLoot.MinSilver:N0} - {result.EstimatedLoot.MaxSilver:N0} silver",
                        Inline = false
                    },
                    new DiscordEmbedField
                    {
                        Name = "Threat Level",
                        Value = result.ThreatLevel.ToString(),
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Fame",
                        Value = $"{result.EstimatedLoot.Fame:N0}",
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Strategy",
                        Value = result.RecommendedStrategy,
                        Inline = false
                    }
                },
                Timestamp = DateTime.Now,
                Footer = new DiscordEmbedFooter
                {
                    Text = "Albion Dungeon Scanner - Avalonian Detection"
                }
            };
        }

        private DiscordEmbed CreateDungeonCompletedEmbed(DungeonCompletedEventArgs e)
        {
            var emoji = e.Type switch
            {
                DungeonType.Avalonian => "👑",
                DungeonType.Corrupted => "💀",
                DungeonType.Group => "👥",
                _ => "🏰"
            };

            return new DiscordEmbed
            {
                Title = $"{emoji} Dungeon Completed",
                Description = $"**{e.Type} Dungeon**",
                Color = 0x00FF00, // Green
                Fields = new List<DiscordEmbedField>
                {
                    new DiscordEmbedField
                    {
                        Name = "Duration",
                        Value = e.Duration.ToString(@"hh\:mm\:ss"),
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Entities Found",
                        Value = e.EntitiesFound.ToString(),
                        Inline = true
                    },
                    new DiscordEmbedField
                    {
                        Name = "Total Value",
                        Value = $"{e.TotalValue:N0} silver",
                        Inline = true
                    }
                },
                Timestamp = DateTime.Now,
                Footer = new DiscordEmbedFooter
                {
                    Text = "Albion Dungeon Scanner - Run Complete"
                }
            };
        }

        private async Task<bool> ValidateWebhookAsync(string webhookUrl)
        {
            try
            {
                // Try to send a minimal message to validate the webhook
                var testPayload = new
                {
                    content = "Test message from Albion Dungeon Scanner - Discord Integration"
                };

                var json = JsonConvert.SerializeObject(testPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(webhookUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task SendDiscordMessage(string title, string description)
        {
            if (string.IsNullOrEmpty(_settings?.WebhookUrl))
                return;

            var embed = new DiscordEmbed
            {
                Title = title,
                Description = description,
                Color = 0x007ACC,
                Timestamp = DateTime.Now,
                Footer = new DiscordEmbedFooter
                {
                    Text = "Albion Dungeon Scanner"
                }
            };

            await SendDiscordEmbed(embed);
        }

        private async Task SendDiscordEmbed(DiscordEmbed embed)
        {
            try
            {
                var payload = new DiscordWebhookPayload
                {
                    Username = "Albion Scanner",
                    Embeds = new[] { embed }
                };

                var json = JsonConvert.SerializeObject(payload, Formatting.None, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_settings.WebhookUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Discord webhook request failed: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Discord embed");
            }
        }

        private Dictionary<string, object> ConvertSettingsToDict(DiscordPluginSettings settings)
        {
            return new Dictionary<string, object>
            {
                ["WebhookUrl"] = settings.WebhookUrl ?? "",
                ["ChannelName"] = settings.ChannelName ?? "",
                ["MinimumValueThreshold"] = settings.MinimumValueThreshold,
                ["NotifyBosses"] = settings.NotifyBosses,
                ["NotifyAvalonianOnly"] = settings.NotifyAvalonianOnly,
                ["NotificationCooldownSeconds"] = settings.NotificationCooldownSeconds,
                ["SendStartupNotifications"] = settings.SendStartupNotifications,
                ["SendShutdownNotifications"] = settings.SendShutdownNotifications,
                ["IncludeDetailedStats"] = settings.IncludeDetailedStats
            };
        }
    }

    // Plugin Settings
    public class DiscordPluginSettings
    {
        public string WebhookUrl { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public int MinimumValueThreshold { get; set; } = 10000;
        public bool NotifyBosses { get; set; } = true;
        public bool NotifyAvalonianOnly { get; set; } = false;
        public int NotificationCooldownSeconds { get; set; } = 30;
        public bool SendStartupNotifications { get; set; } = true;
        public bool SendShutdownNotifications { get; set; } = true;
        public bool IncludeDetailedStats { get; set; } = true;
    }

    // Discord API Models
    public class DiscordWebhookPayload
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("avatar_url")]
        public string AvatarUrl { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("embeds")]
        public DiscordEmbed[] Embeds { get; set; }
    }

    public class DiscordEmbed
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("color")]
        public int Color { get; set; }

        [JsonProperty("fields")]
        public List<DiscordEmbedField> Fields { get; set; }

        [JsonProperty("footer")]
        public DiscordEmbedFooter Footer { get; set; }

        [JsonProperty("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonProperty("thumbnail")]
        public DiscordEmbedThumbnail Thumbnail { get; set; }
    }

    public class DiscordEmbedField
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("inline")]
        public bool Inline { get; set; }
    }

    public class DiscordEmbedFooter
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("icon_url")]
        public string IconUrl { get; set; }
    }

    public class DiscordEmbedThumbnail
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }
}