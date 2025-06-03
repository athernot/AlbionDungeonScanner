using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AlbionDungeonScanner.Core.Models;
using AlbionDungeonScanner.Core.Scanner;
using AlbionDungeonScanner.Core.Plugins;
using AlbionDungeonScanner.Core.Avalonian;

namespace AlbionDungeonScanner.GUI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MainWindow> _logger;
        private readonly DungeonScanner _scanner;
        private readonly PluginManager _pluginManager;
        private readonly AvalonianDetector _avalonianDetector;
        
        private readonly DispatcherTimer _updateTimer;
        private readonly ObservableCollection<EntityDisplayModel> _entityList;
        private readonly Dictionary<string, UIElement> _mapElements;
        
        private bool _isScanning = false;
        private DateTime _scanStartTime;
        private int _packetCount = 0;
        private AvalonianDungeonMap _currentMap;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetRequiredService<ILogger<MainWindow>>();
            _scanner = serviceProvider.GetRequiredService<DungeonScanner>();
            _pluginManager = serviceProvider.GetRequiredService<PluginManager>();
            _avalonianDetector = serviceProvider.GetRequiredService<AvalonianDetector>();
            
            _entityList = new ObservableCollection<EntityDisplayModel>();
            _mapElements = new Dictionary<string, UIElement>();
            
            InitializeUI();
            SetupEventHandlers();
            SetupTimer();
        }

        private void InitializeUI()
        {
            DataContext = this;
            EntityDataGrid.ItemsSource = _entityList;
            
            // Set initial status
            UpdateStatus("Ready", false);
            UpdateStatusBar("Scanner ready. Click 'Start Scanner' to begin monitoring.");
        }

        private void SetupEventHandlers()
        {
            // Scanner events
            _scanner.EntityDetected += OnEntityDetected;
            _scanner.EntityRemoved += OnEntityRemoved;
            _scanner.AvalonianEntityDetected += OnAvalonianEntityDetected;
            _scanner.ScannerStatusChanged += OnScannerStatusChanged;
            _scanner.PacketProcessed += OnPacketProcessed;
            
            // Window events
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;
        }

        private void SetupTimer()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromSeconds(1);
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.LogInformation("Initializing main window");
                
                // Load plugins
                await _pluginManager.LoadPluginsAsync();
                
                // Initialize map
                InitializeMap();
                
                _logger.LogInformation("Main window initialization complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during main window initialization");
                MessageBox.Show($"Initialization error: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeMap()
        {
            // Set up map canvas
            DungeonMapCanvas.Children.Clear();
            _mapElements.Clear();
            
            // Add grid lines for reference
            AddMapGridLines();
        }

        private void AddMapGridLines()
        {
            var gridSize = 50;
            var mapWidth = DungeonMapCanvas.Width;
            var mapHeight = DungeonMapCanvas.Height;
            
            for (int x = 0; x <= mapWidth; x += gridSize)
            {
                var line = new Line
                {
                    X1 = x, Y1 = 0,
                    X2 = x, Y2 = mapHeight,
                    Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    StrokeThickness = 1
                };
                DungeonMapCanvas.Children.Add(line);
            }
            
            for (int y = 0; y <= mapHeight; y += gridSize)
            {
                var line = new Line
                {
                    X1 = 0, Y1 = y,
                    X2 = mapWidth, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    StrokeThickness = 1
                };
                DungeonMapCanvas.Children.Add(line);
            }
        }

        private async void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isScanning)
            {
                await StartScanning();
            }
            else
            {
                await StopScanning();
            }
        }

        private async Task StartScanning()
        {
            try
            {
                _logger.LogInformation("Starting scanner");
                
                UpdateStatusBar("Starting scanner...");
                StartStopButton.IsEnabled = false;
                
                await _scanner.StartAsync();
                
                _isScanning = true;
                _scanStartTime = DateTime.Now;
                _packetCount = 0;
                
                StartStopButton.Content = "Stop Scanner";
                StartStopButton.IsEnabled = true;
                
                UpdateStatus("Connected", true);
                UpdateStatusBar("Scanner active. Monitoring network traffic...");
                
                _updateTimer.Start();
                
                _logger.LogInformation("Scanner started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting scanner");
                
                _isScanning = false;
                StartStopButton.Content = "Start Scanner";
                StartStopButton.IsEnabled = true;
                
                UpdateStatus("Error", false);
                UpdateStatusBar($"Error starting scanner: {ex.Message}");
                
                MessageBox.Show($"Failed to start scanner: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopScanning()
        {
            try
            {
                _logger.LogInformation("Stopping scanner");
                
                UpdateStatusBar("Stopping scanner...");
                StartStopButton.IsEnabled = false;
                
                _updateTimer.Stop();
                await _scanner.StopAsync();
                
                _isScanning = false;
                
                StartStopButton.Content = "Start Scanner";
                StartStopButton.IsEnabled = true;
                
                UpdateStatus("Disconnected", false);
                UpdateStatusBar("Scanner stopped.");
                
                _logger.LogInformation("Scanner stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping scanner");
                
                StartStopButton.IsEnabled = true;
                UpdateStatusBar($"Error stopping scanner: {ex.Message}");
            }
        }

        private void OnEntityDetected(object sender, EntityDetectedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Add to entity list
                var displayModel = new EntityDisplayModel(e.Entity);
                _entityList.Add(displayModel);
                
                // Add to map
                AddEntityToMap(e.Entity);
                
                // Update statistics
                UpdateEntityStatistics();
                
                _logger.LogDebug("Entity detected: {EntityName} at {Position}", 
                                e.Entity.Name, e.Entity.Position);
            });
        }

        private void OnEntityRemoved(object sender, EntityRemovedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Remove from entity list
                var item = _entityList.FirstOrDefault(x => x.Id == e.Entity.Id);
                if (item != null)
                {
                    _entityList.Remove(item);
                }
                
                // Remove from map
                RemoveEntityFromMap(e.Entity.Id);
                
                // Update statistics
                UpdateEntityStatistics();
            });
        }

        private void OnAvalonianEntityDetected(object sender, AvalonianEntityEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update Avalonian display
                UpdateAvalonianDetails(e.Result);
                
                // Highlight on map with special color
                HighlightAvalonianEntity(e.Result);
                
                _logger.LogInformation("Avalonian entity detected: {EntityName}", 
                                      e.Result.EntityData.Name);
            });
        }

        private void OnScannerStatusChanged(object sender, ScannerStatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus(e.Status, e.IsConnected);
                UpdateStatusBar(e.Message);
            });
        }

        private void OnPacketProcessed(object sender, PacketProcessedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _packetCount++;
                PacketCountText.Text = $"Packets: {_packetCount:N0}";
            });
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_isScanning)
            {
                var elapsed = DateTime.Now - _scanStartTime;
                ScanTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
            }
        }

        private void UpdateStatus(string status, bool isConnected)
        {
            StatusText.Text = status;
            
            var color = status switch
            {
                "Connected" => Colors.LimeGreen,
                "Disconnected" => Colors.Gray,
                "Error" => Colors.Red,
                "Warning" => Colors.Orange,
                _ => Colors.Gray
            };
            
            StatusIndicator.Fill = new SolidColorBrush(color);
        }

        private void UpdateStatusBar(string message)
        {
            StatusBarText.Text = message;
        }

        private void AddEntityToMap(DungeonEntity entity)
        {
            if (_mapElements.ContainsKey(entity.Id))
                return;

            var mapX = ConvertWorldToMapX(entity.Position.X);
            var mapY = ConvertWorldToMapY(entity.Position.Z);
            
            var element = CreateMapElement(entity);
            Canvas.SetLeft(element, mapX);
            Canvas.SetTop(element, mapY);
            
            DungeonMapCanvas.Children.Add(element);
            _mapElements[entity.Id] = element;
        }

        private UIElement CreateMapElement(DungeonEntity entity)
        {
            var color = GetEntityColor(entity);
            var size = GetEntitySize(entity);
            
            var ellipse = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1,
                ToolTip = $"{entity.Name}\nType: {entity.Type}\nPosition: ({entity.Position.X:F1}, {entity.Position.Z:F1})"
            };
            
            // Add glow effect for high-value entities
            if (entity.DungeonType == DungeonType.Avalonian)
            {
                ellipse.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Gold,
                    ShadowDepth = 0,
                    BlurRadius = 10
                };
            }
            
            return ellipse;
        }

        private Color GetEntityColor(DungeonEntity entity)
        {
            return entity.Type switch
            {
                EntityType.Chest => entity.DungeonType == DungeonType.Avalonian ? Colors.Gold : Colors.LimeGreen,
                EntityType.Boss => Colors.Red,
                EntityType.Mob => Colors.Orange,
                EntityType.ResourceNode => Colors.Blue,
                EntityType.Portal => Colors.Purple,
                _ => Colors.Gray
            };
        }

        private double GetEntitySize(DungeonEntity entity)
        {
            return entity.Type switch
            {
                EntityType.Boss => 16,
                EntityType.Chest when entity.DungeonType == DungeonType.Avalonian => 14,
                EntityType.Chest => 12,
                EntityType.Mob => 10,
                _ => 8
            };
        }

        private void RemoveEntityFromMap(string entityId)
        {
            if (_mapElements.TryGetValue(entityId, out var element))
            {
                DungeonMapCanvas.Children.Remove(element);
                _mapElements.Remove(entityId);
            }
        }

        private void HighlightAvalonianEntity(AvalonianScanResult result)
        {
            if (_mapElements.TryGetValue(result.EntityData.Name, out var element) && element is Ellipse ellipse)
            {
                // Add special highlighting for Avalonian entities
                ellipse.Fill = new SolidColorBrush(Colors.Gold);
                ellipse.StrokeThickness = 3;
                
                // Add to high priority list
                AddToHighPriorityList(result);
            }
        }

        private void AddToHighPriorityList(AvalonianScanResult result)
        {
            if (result.EntityData.Priority >= ScanPriority.High)
            {
                var priorityItems = HighPriorityList.ItemsSource as ObservableCollection<HighPriorityItem> 
                                   ?? new ObservableCollection<HighPriorityItem>();
                
                priorityItems.Add(new HighPriorityItem
                {
                    Name = result.EntityData.Name,
                    Strategy = result.RecommendedStrategy,
                    Value = result.EstimatedLoot.MaxSilver
                });
                
                HighPriorityList.ItemsSource = priorityItems;
            }
        }

        private void UpdateEntityStatistics()
        {
            var totalEntities = _entityList.Count;
            var totalValue = _entityList.Sum(e => e.EstimatedValue);
            
            TotalEntitiesText.Text = totalEntities.ToString();
            TotalValueText.Text = $"{totalValue:N0}";
        }

        private void UpdateAvalonianDetails(AvalonianScanResult result)
        {
            // Update completion progress
            _currentMap = _avalonianDetector.GenerateMap();
            if (_currentMap != null)
            {
                CompletionProgressBar.Value = _currentMap.CompletionPercentage;
                CompletionPercentageText.Text = $"{_currentMap.CompletionPercentage:F1}%";
                RoomsFoundText.Text = _currentMap.Rooms.Count.ToString();
                
                var estimatedTotal = 15; // Average Avalonian dungeon size
                var remaining = Math.Max(0, estimatedTotal - _currentMap.Rooms.Count);
                RemainingRoomsText.Text = remaining.ToString();
            }
            
            // Update threat assessment
            var bosses = _entityList.Count(e => e.Type == "Boss");
            var threatLevel = CalculateOverallThreat();
            
            BossCountText.Text = bosses.ToString();
            ThreatLevelText.Text = threatLevel;
            ThreatLevelText.Foreground = GetThreatColor(threatLevel);
        }

        private string CalculateOverallThreat()
        {
            var bosses = _entityList.Count(e => e.Type == "Boss");
            var elites = _entityList.Count(e => e.Type == "Elite Mob");
            
            if (bosses >= 3) return "Extreme";
            if (bosses >= 2) return "High";
            if (bosses >= 1 || elites >= 5) return "Medium";
            return "Low";
        }

        private Brush GetThreatColor(string threatLevel)
        {
            return threatLevel switch
            {
                "Extreme" => new SolidColorBrush(Colors.DarkRed),
                "High" => new SolidColorBrush(Colors.Red),
                "Medium" => new SolidColorBrush(Colors.Orange),
                "Low" => new SolidColorBrush(Colors.LimeGreen),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        private double ConvertWorldToMapX(float worldX)
        {
            // Convert world coordinates to map canvas coordinates
            return (worldX + 1000) * (DungeonMapCanvas.Width / 2000);
        }

        private double ConvertWorldToMapY(float worldZ)
        {
            // Convert world coordinates to map canvas coordinates  
            return (worldZ + 1000) * (DungeonMapCanvas.Height / 2000);
        }

        // Event handlers for UI controls
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_serviceProvider);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void PluginsButton_Click(object sender, RoutedEventArgs e)
        {
            var pluginsWindow = new PluginsWindow(_serviceProvider);
            pluginsWindow.Owner = this;
            pluginsWindow.ShowDialog();
        }

        private void CenterMapButton_Click(object sender, RoutedEventArgs e)
        {
            MapScrollViewer.ScrollToHorizontalOffset(0);
            MapScrollViewer.ScrollToVerticalOffset(0);
            MapScrollViewer.ZoomToFit();
        }

        private void ExportMapButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement map export functionality
            MessageBox.Show("Map export functionality will be implemented in a future version.",
                           "Feature Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void GeneratePathButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMap != null)
            {
                DrawOptimalPath(_currentMap.RecommendedPath);
            }
        }

        private void DrawOptimalPath(List<Vector3> path)
        {
            // Remove existing path
            var existingPaths = DungeonMapCanvas.Children.OfType<Path>().ToList();
            foreach (var pathElement in existingPaths)
            {
                DungeonMapCanvas.Children.Remove(pathElement);
            }
            
            if (path.Count < 2) return;
            
            // Draw path lines
            for (int i = 0; i < path.Count - 1; i++)
            {
                var line = new Line
                {
                    X1 = ConvertWorldToMapX(path[i].X),
                    Y1 = ConvertWorldToMapY(path[i].Z),
                    X2 = ConvertWorldToMapX(path[i + 1].X),
                    Y2 = ConvertWorldToMapY(path[i + 1].Z),
                    Stroke = new SolidColorBrush(Colors.Cyan),
                    StrokeThickness = 3,
                    StrokeDashArray = new DoubleCollection { 5, 5 }
                };
                
                DungeonMapCanvas.Children.Add(line);
            }
        }

        private void ExportDataButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement data export functionality
            MessageBox.Show("Data export functionality will be implemented in a future version.",
                           "Feature Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void EntityDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntityDataGrid.SelectedItem is EntityDisplayModel selectedEntity)
            {
                // Center map on selected entity
                var mapX = ConvertWorldToMapX(selectedEntity.Position.X);
                var mapY = ConvertWorldToMapY(selectedEntity.Position.Z);
                
                MapScrollViewer.ScrollToHorizontalOffset(mapX - MapScrollViewer.ViewportWidth / 2);
                MapScrollViewer.ScrollToVerticalOffset(mapY - MapScrollViewer.ViewportHeight / 2);
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MapScrollViewer != null)
            {
                MapScrollViewer.ZoomToFactor(e.NewValue);
            }
        }

        private void DungeonMapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = e.GetPosition(DungeonMapCanvas);
            
            // Convert canvas coordinates back to world coordinates for debugging
            var worldX = (position.X / DungeonMapCanvas.Width * 2000) - 1000;
            var worldZ = (position.Y / DungeonMapCanvas.Height * 2000) - 1000;
            
            UpdateStatusBar($"Map clicked at world coordinates: ({worldX:F1}, {worldZ:F1})");
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                if (_isScanning)
                {
                    await StopScanning();
                }
                
                _pluginManager?.Dispose();
                _updateTimer?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during window closing");
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Supporting data models for UI binding
    public class EntityDisplayModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int Tier { get; set; }
        public int EstimatedValue { get; set; }
        public Brush ValueColor { get; set; }
        public Vector3 Position { get; set; }
        public DateTime LastSeen { get; set; }

        public EntityDisplayModel(DungeonEntity entity)
        {
            Id = entity.Id;
            Name = entity.Name;
            Type = entity.Type.ToString();
            Position = entity.Position;
            LastSeen = entity.LastSeen;
            
            // Calculate estimated value and tier based on entity type
            (Tier, EstimatedValue) = CalculateEntityValue(entity);
            ValueColor = GetValueColor(EstimatedValue);
        }

        private (int tier, int value) CalculateEntityValue(DungeonEntity entity)
        {
            // Simplified value calculation - could be enhanced with real data
            return entity.Type switch
            {
                EntityType.Chest when entity.DungeonType == DungeonType.Avalonian => (6, 15000),
                EntityType.Chest => (4, 5000),
                EntityType.Boss when entity.DungeonType == DungeonType.Avalonian => (7, 50000),
                EntityType.Boss => (5, 20000),
                EntityType.Mob => (4, 1000),
                _ => (1, 100)
            };
        }

        private Brush GetValueColor(int value)
        {
            return value switch
            {
                >= 30000 => new SolidColorBrush(Colors.Gold),
                >= 15000 => new SolidColorBrush(Colors.Orange),
                >= 5000 => new SolidColorBrush(Colors.Yellow),
                >= 1000 => new SolidColorBrush(Colors.LightGreen),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
    }

    public class HighPriorityItem
    {
        public string Name { get; set; }
        public string Strategy { get; set; }
        public int Value { get; set; }
    }

    // Event argument classes
    public class EntityDetectedEventArgs : EventArgs
    {
        public DungeonEntity Entity { get; set; }
    }

    public class EntityRemovedEventArgs : EventArgs
    {
        public DungeonEntity Entity { get; set; }
    }

    public class AvalonianEntityEventArgs : EventArgs
    {
        public AvalonianScanResult Result { get; set; }
    }

    public class ScannerStatusEventArgs : EventArgs
    {
        public string Status { get; set; }
        public bool IsConnected { get; set; }
        public string Message { get; set; }
    }

    public class PacketProcessedEventArgs : EventArgs
    {
        public int PacketCount { get; set; }
    }
}