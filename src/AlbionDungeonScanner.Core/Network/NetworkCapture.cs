using System;
using System.Collections.Generic;
using System.Linq;
using SharpPcap;
using PacketDotNet; // Pastikan ini ada di .csproj jika belum
using AlbionDungeonScanner.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Network
{
    public class NetworkCapture : IDisposable
    {
        private ICaptureDevice _device;
        private readonly PhotonPacketParser _parser; // Akan di-inject
        private readonly ILogger<NetworkCapture> _logger;
        private bool _isCapturing;

        public event Action<PhotonEvent> GameEventReceived; // Ganti nama agar lebih spesifik
        public event Action<string> StatusChanged;

        public bool IsCapturing => _isCapturing;

        public NetworkCapture(ILogger<NetworkCapture> logger, PhotonPacketParser parser) // Terima parser via DI
        {
            _parser = parser;
            _logger = logger;
        }

        public bool StartCapture(string interfaceName = null)
        {
            try
            {
                // ... (logika pemilihan device tetap sama) ...
                var devices = CaptureDeviceList.Instance;
                if (devices.Count < 1)
                {
                    _logger?.LogError("No capture devices found!");
                    StatusChanged?.Invoke("No network devices found");
                    return false;
                }

                if (string.IsNullOrEmpty(interfaceName) || interfaceName.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    _device = devices.FirstOrDefault(d => d.Addresses.Any(a => a.Addr != null && a.Addr.ipAddress != null && !IPAddress.IsLoopback(a.Addr.ipAddress))) ?? devices.FirstOrDefault();
                }
                else
                {
                    _device = devices.FirstOrDefault(d => (d.Name != null && d.Name.Contains(interfaceName, StringComparison.OrdinalIgnoreCase)) || 
                                                        (d.Description != null && d.Description.Contains(interfaceName, StringComparison.OrdinalIgnoreCase)));
                }
                
                if (_device == null && devices.Count > 0)
                {
                     _logger?.LogWarning("Specified interface not found, falling back to the first available device.");
                    _device = devices[0];
                }
                 else if (_device == null)
                {
                    _logger?.LogError("No suitable capture device found or selected interface is invalid.");
                    StatusChanged?.Invoke("No suitable network device found");
                    return false;
                }


                _device.OnPacketArrival += OnPacketArrival;
                _device.Open(DeviceMode.Promiscuous, 1000); // Baca timeout 1000ms
                
                // Filter untuk Albion Online traffic (biasanya port UDP 5055 atau 5056)
                // Periksa apakah game menggunakan port lain atau TCP jika UDP tidak menangkap apa pun.
                _device.Filter = "udp port 5055 or udp port 5056"; 
                
                _device.StartCapture();
                _isCapturing = true;
                
                _logger?.LogInformation("Packet capture started on device: {DeviceDescription}", _device.Description);
                StatusChanged?.Invoke($"Capturing on: {_device.Description}");
                
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
                    // _device.OnPacketArrival -= OnPacketArrival; // Dihapus saat device di-dispose
                    _isCapturing = false;
                    
                    _logger?.LogInformation("Packet capture stopped.");
                    StatusChanged?.Invoke("Capture stopped.");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping packet capture.");
            }
        }

        private void OnPacketArrival(object sender, CaptureEventArgs e)
        {
            try
            {
                var packet = Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data);
                var udpPacket = packet.Extract<UdpPacket>();
                
                if (udpPacket != null && udpPacket.PayloadData != null && udpPacket.PayloadData.Length > 0)
                {
                    // Langsung parse seluruh payload UDP sebagai satu message Photon
                    PhotonEvent photonEvent = _parser.ParseMessage(udpPacket.PayloadData);
                    if (photonEvent != null)
                    {
                        GameEventReceived?.Invoke(photonEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error processing raw packet arrival.");
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
            if (_device != null)
            {
                _device.OnPacketArrival -= OnPacketArrival; // Pastikan unsubscribe
                _device.Dispose(); // SharpPcap device mungkin tidak IDisposable, tapi jika ya, ini baik
                _device = null;
            }
            _logger?.LogInformation("NetworkCapture disposed.");
        }
    }
}