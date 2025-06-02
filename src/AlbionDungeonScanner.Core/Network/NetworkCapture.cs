using System;
using System.Collections.Generic;
using System.Linq;
using SharpPcap;
using PacketDotNet;
using AlbionDungeonScanner.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Network
{
    public class NetworkCapture
    {
        private ICaptureDevice _device;
        private readonly PhotonPacketParser _parser;
        private readonly ILogger<NetworkCapture> _logger;
        private bool _isCapturing;

        public event Action<PhotonEvent> PacketReceived;
        public event Action<string> StatusChanged;

        public bool IsCapturing => _isCapturing;

        public NetworkCapture(ILogger<NetworkCapture> logger = null)
        {
            _parser = new PhotonPacketParser();
            _logger = logger;
        }

        public bool StartCapture(string interfaceName = null)
        {
            try
            {
                var devices = CaptureDeviceList.Instance;
                if (devices.Count < 1)
                {
                    _logger?.LogError("No capture devices found!");
                    StatusChanged?.Invoke("No network devices found");
                    return false;
                }

                // Select device
                if (string.IsNullOrEmpty(interfaceName) || interfaceName == "auto")
                {
                    _device = devices.FirstOrDefault(d => d.Started == false);
                }
                else
                {
                    _device = devices.FirstOrDefault(d => d.Name.Contains(interfaceName) || d.Description.Contains(interfaceName));
                }

                if (_device == null)
                {
                    _device = devices[0]; // Fallback to first device
                }

                _device.OnPacketArrival += OnPacketArrival;
                _device.Open(DeviceMode.Promiscuous, 1000);
                
                // Filter untuk Albion Online traffic (biasanya port 5055 atau 5056)
                _device.Filter = "udp port 5055 or udp port 5056";
                
                _device.StartCapture();
                _isCapturing = true;
                
                _logger?.LogInformation($"Packet capture started on device: {_device.Description}");
                StatusChanged?.Invoke($"Capturing on {_device.Description}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to start packet capture");
                StatusChanged?.Invoke($"Capture failed: {ex.Message}");
                return false;
            }
        }

        public void StopCapture()
        {
            try
            {
                if (_device != null && _isCapturing)
                {
                    _device.StopCapture();
                    _device.Close();
                    _device.OnPacketArrival -= OnPacketArrival;
                    _isCapturing = false;
                    
                    _logger?.LogInformation("Packet capture stopped");
                    StatusChanged?.Invoke("Capture stopped");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping packet capture");
            }
        }

        private void OnPacketArrival(object sender, CaptureEventArgs e)
        {
            try
            {
                var packet = Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data);
                var udpPacket = packet.Extract<UdpPacket>();
                
                if (udpPacket != null)
                {
                    var photonEvent = _parser.ParsePacket(udpPacket.PayloadData);
                    if (photonEvent != null)
                    {
                        PacketReceived?.Invoke(photonEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"Error processing packet: {ex.Message}");
            }
        }

        public List<string> GetAvailableInterfaces()
        {
            var interfaces = new List<string>();
            
            try
            {
                var devices = CaptureDeviceList.Instance;
                foreach (var device in devices)
                {
                    interfaces.Add($"{device.Name} - {device.Description}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting network interfaces");
            }

            return interfaces;
        }

        public void Dispose()
        {
            StopCapture();
            _device?.Dispose();
        }
    }
}