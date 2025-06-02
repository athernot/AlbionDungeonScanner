using System;
using System.Collections.Generic;
using AlbionDungeonScanner.Core.Models;
using Newtonsoft.Json;

namespace AlbionDungeonScanner.Core.Network
{
    public class PhotonPacketParser
    {
        private readonly Dictionary<byte, string> _operationCodes;
        private readonly Dictionary<byte, string> _eventCodes;

        public PhotonPacketParser()
        {
            // Operation codes dari AlbionTracker/Albion.Common/Photon/OperationCodes.cs
            _operationCodes = new Dictionary<byte, string>
            {
                { 1, "Join" },
                { 2, "Leave" },
                { 3, "Move" },
                { 4, "ChangeEquipment" },
                { 5, "Attack" },
                { 6, "CastStart" },
                { 7, "CastCancel" },
                { 8, "TerminateToggleSpell" },
                { 9, "ChannelingCancel" },
                { 10, "CastHit" },
                { 11, "CastHits" },
                { 12, "CastTimeUpdate" },
                { 13, "HealthUpdate" },
                { 14, "EnergyUpdate" },
                { 15, "DamageShieldUpdate" },
                { 16, "CraftingFocusUpdate" },
                { 17, "ActiveSpellEffectsUpdate" },
                { 18, "ResetCooldowns" },
                { 19, "NewCharacter" },
                { 20, "NewEquipmentItem" },
                { 21, "NewSimpleItem" },
                { 22, "NewFurnitureItem" },
                { 23, "NewKillTrophyItem" },
                { 24, "NewJournalItem" },
                { 25, "NewLaborerItem" }
            };

            // Event codes untuk tracking entities
            _eventCodes = new Dictionary<byte, string>
            {
                { 1, "NewMob" },
                { 2, "NewPlayer" },
                { 3, "NewChest" },
                { 4, "MobDied" },
                { 5, "ChestOpened" },
                { 6, "NewHarvestableObject" },
                { 7, "NewBuilding" },
                { 8, "NewTreasure" },
                { 9, "UpdateFame" },
                { 10, "UpdateSilver" },
                { 11, "UpdateReSpecPoints" },
                { 12, "UpdateCurrency" },
                { 13, "UpdateFactionStanding" },
                { 14, "UpdateLearningPoints" },
                { 15, "UpdateReputation" },
                { 16, "UpdateCrafter" },
                { 17, "UpdatePlayerArena" },
                { 18, "UpdateGuildArena" },
                { 19, "UpdateItems" },
                { 20, "NewLoot" }
            };
        }

        public PhotonEvent ParsePacket(byte[] data)
        {
            try
            {
                if (data == null || data.Length < 8)
                    return null;

                // Basic packet structure validation
                if (data[0] != 0xF3) // Photon magic number
                    return null;

                // Extract command type
                var commandType = data[1];
                
                // For now, we'll focus on reliable messages (commandType == 6)
                if (commandType != 6)
                    return null;

                // Extract operation/event code (simplified)
                var operationCode = data[7];
                
                // Create basic PhotonEvent
                var photonEvent = new PhotonEvent
                {
                    Code = operationCode,
                    Parameters = ExtractParameters(data)
                };

                return photonEvent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing packet: {ex.Message}");
                return null;
            }
        }

        private Dictionary<byte, object> ExtractParameters(byte[] data)
        {
            var parameters = new Dictionary<byte, object>();
            
            try
            {
                // Simplified parameter extraction
                // In a real implementation, this would need proper Photon protocol parsing
                
                if (data.Length > 12)
                {
                    // Mock entity ID
                    parameters[1] = System.Text.Encoding.UTF8.GetString(data, 12, Math.Min(32, data.Length - 12)).Trim('\0');
                    
                    // Mock position data
                    if (data.Length > 20)
                    {
                        parameters[2] = BitConverter.ToSingle(data, 16); // X position
                        parameters[3] = BitConverter.ToSingle(data, 20); // Y position
                        
                        if (data.Length > 24)
                            parameters[4] = BitConverter.ToSingle(data, 24); // Z position
                    }
                }
            }
            catch
            {
                // Return empty parameters if parsing fails
            }

            return parameters;
        }

        public string GetOperationName(byte code)
        {
            return _operationCodes.TryGetValue(code, out var name) ? name : $"Unknown_{code}";
        }

        public string GetEventName(byte code)
        {
            return _eventCodes.TryGetValue(code, out var name) ? name : $"Unknown_{code}";
        }
    }
}