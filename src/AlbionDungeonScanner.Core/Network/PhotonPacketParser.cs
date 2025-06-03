using System;
using System.Collections.Generic;
using AlbionDungeonScanner.Core.Models;
using ExitGames.Client.Photon; // WAJIB ADA: Asumsikan Anda memiliki referensi ke library Photon (misalnya Photon3DotNet.dll)
using Microsoft.Extensions.Logging;

namespace AlbionDungeonScanner.Core.Network
{
    public class PhotonPacketParser
    {
        private readonly ILogger<PhotonPacketParser> _logger;
        private readonly Protocol16 _protocol16; // Instance dari deserializer Photon

        // Menggunakan EventCodes dari AlbionTracker/Albion.Common/Photon/EventCodes.cs
        // https://github.com/0blu/AlbionTracker/blob/master/Albion.Common/Photon/EventCodes.cs
        private readonly Dictionary<byte, string> _eventCodes = new Dictionary<byte, string>
        {
            {0, "Leave"},
            {1, "JoinFinished"},
            {2, "Move"},
            {3, "Teleport"},
            {4, "ChangeEquipment"},
            {5, "HealthUpdate"},
            {6, "EnergyUpdate"},
            {7, "DamageShieldUpdate"},
            {8, "CraftingFocusUpdate"},
            {10, "ActiveSpellEffectsUpdate"},
            {11, "ResetCooldowns"},
            {12, "Attack"},
            {13, "CastStart"},
            {14, "CastCancel"},
            {15, "CastTimeUpdate"},
            {16, "CastFinished"},
            {17, "CastSpell"},
            {18, "CastSpells"},
            {19, "ChannelingCancel"},
            {20, "AttackFinished"},
            {21, "TakeDamage"},
            {22, "UpdateFame"},
            {23, "UpdateLearningPoints"},
            {24, "UpdateReSpecPoints"},
            {25, "UpdateSilver"},
            {26, "UpdateGold"},
            {27, "UpdateFactionStanding"},
            {28, "UpdateReputation"},
            {29, "UpdateMight"},
            {30, "UpdateFavor"},
            {31, "Respawn"},
            {32, "ServerDebugLog"},
            {33, "CharacterEquipmentChanged"},
            {34, "RegenerationHealthChanged"},
            {35, "RegenerationEnergyChanged"},
            {36, "RegenerationMountHealthChanged"},
            {37, "Mounted"},
            {38, "MountStart"},
            {39, "MountCancel"},
            {40, "Unmounted"},
            {41, "NewCharacter"},
            {42, "NewEquipmentItem"},
            {43, "NewSimpleItem"},
            {44, "NewFurnitureItem"},
            {45, "NewKillTrophyItem"},
            {46, "NewJournalItem"},
            {47, "NewLaborerItem"},
            {48, "NewSimpleHarvestableObject"},
            {49, "NewSimpleHarvestableObjectList"},
            {50, "NewHarvestableObject"},
            {51, "NewTreasureChest"},
            {52, "NewSilverObject"},
            {53, "NewMob"},
            {54, "NewPlayer"},
            {55, "NewNpc"},
            {56, "NewMount"},
            {57, "NewLoot"},
            {58, "NewDisconnectedPlayer"},
            {59, "NewOrb"},
            {60, "NewCastle"},
            {61, "NewMists"},
            {62, "NewCorruptedDungeon"},
            {63, "NewBuilding"},
            {64, "NewIsland"},
            {65, "NewIslandAccessPoint"},
            {66, "NewGuildBanner"},
            {67, "NewMistsImmediateReturn"},
            {68, "NewOther"},
            {69, "NewExpedition"},
            {70, "NewArena"},
            {71, "NewHellgate"},
            {72, "NewOutlands"},
            {73, "NewCrystalLeague"},
            {74, "OtherGrabbedLoot"},
            {75, "MutePlayer"},
            {76, "UnmutePlayer"},
            {77, "GuildInvite"},
            {78, "AllianceInvite"},
            {79, "FriendInvite"},
            {80, "DuelInvite"},
            {81, "PartyInvite"},
            {82, "ItemsRestack"},
            {83, "ItemCooldownUpdate"},
            {85, "GuildVaultInfo"},
            {86, "GuildFundChanged"},
            {87, "GuildPlayerChanged"},
            {88, "GuildManagement"},
            {89, "GuildRoleChanged"},
            {90, "AllianceManagement"},
            {91, "ChatMessage"},
            {92, "ChatSay"},
            {93, "ChatWhisper"},
            {94, "ChatGuild"},
            {95, "ChatAlliance"},
            {96, "ChatParty"},
            {97, "ChatSystem"},
            {98, "ChatBattle"},
            {99, "ChatLocal"},
            {100, "ChatChannel"},
            {101, "ChatReport"},
            {102, "ChatHelp"},
            {103, "ChatError"},
            {104, "ChatAdmin"},
            {105, "ChatGlobal"},
            {106, "ChatTrade"},
            {107, "ChatLookingForGroup"},
            {108, "ChatRecruitment"},
            {109, "ChatReportSent"},
            {110, "ChatReportReceived"},
            {111, "ChatPlayerMuted"},
            {112, "ChatPlayerUnmuted"},
            {113, "ChangeAvatar"},
            {114, "ChangePlayerIsland"},
            {115, "ChangeGuildIsland"},
            {116, "ChangeHome"},
            {117, "LeaveHome"},
            {118, "ReenterHome"},
            {119, "ClaimPlayerIsland"},
            {120, "ClaimGuildIsland"},
            {121, "AbandonPlayerIsland"},
            {122, "AbandonGuildIsland"},
            {123, "UpgradePlayerIsland"},
            {124, "UpgradeGuildIsland"},
            {125, "GivePlayerIslandAccessRight"},
            {126, "GiveGuildIslandAccessRight"},
            {127, "RevokePlayerIslandAccessRight"},
            {128, "RevokeGuildIslandAccessRight"},
            {129, "PlayerIslandAccessRightsChanged"},
            {130, "GuildIslandAccessRightsChanged"},
            {131, "PlayerIslandVisitorListChanged"},
            {132, "GuildIslandVisitorListChanged"},
            {133, "PlayerIslandSettingsChanged"},
            {134, "GuildIslandSettingsChanged"},
            {135, "PlayerIslandBuildingChanged"},
            {136, "GuildIslandBuildingChanged"},
            {137, "NewTravelpoint"},
            {138, "NewPortalEntrance"},
            {139, "NewPortalExit"},
            {140, "NewRandomDungeonExit"},
            {141, "NewExpeditionExit"},
            {142, "NewArenaExit"},
            {143, "NewHellgateExit"},
            {144, "NewOutlandsExit"},
            {145, "NewCrystalLeagueExit"},
            {146, "NewCorruptedDungeonExit"},
            {147, "NewMistsExit"},
            {148, "NewSiegeCamp"},
            {149, "NewTerritory"},
            {150, "NewWatchtower"},
            {151, "NewFarmable"},
            {152, "NewHideout"},
            {153, "NewOpenWorldChest"},
            {154, "NewTutorialBlocker"},
            {155, "NewCityBuilding"},
            {156, "NewPlayerHouse"},
            {157, "NewGuildHouse"},
            {158, "NewMarketplace"},
            {159, "NewRepairStation"},
            {160, "NewArtifactFoundry"},
            {161, "NewResourceSite"},
            {162, "NewFishingZone"},
            {163, "NewTutorial"},
            {164, "NewTutorialNpc"},
            {165, "NewTutorialMob"},
            {166, "NewTutorialChest"},
            {167, "NewTutorialResource"},
            {168, "NewTutorialPortal"},
            {169, "NewTutorialExit"},
            {170, "NewTutorialBlockerExit"},
            {171, "NewDynamicEvent"},
            {172, "NewRoad"},
            {173, "NewRoadExit"},
            {174, "MinimapZergs"},
            {175, "MinimapSmartCluster"},
            {176, "MinimapLocalPlayer"},
            {177, "MinimapPlayer"},
            {178, "MinimapGuildMember"},
            {179, "MinimapAllianceMember"},
            {180, "MinimapPartyMember"},
            {181, "MinimapEnemyPlayer"},
            {182, "MinimapFlaggedPlayer"},
            {183, "MinimapHostilePlayer"},
            {184, "MinimapMob"},
            {185, "MinimapBoss"},
            {186, "MinimapEliteMob"},
            {187, "MinimapHarvestable"},
            {188, "MinimapTreasure"},
            {189, "MinimapSilver"},
            {190, "MinimapPortal"},
            {191, "MinimapExit"},
            {192, "MinimapHellgate"},
            {193, "MinimapDungeon"},
            {194, "MinimapExpedition"},
            {195, "MinimapArena"},
            {196, "MinimapOutlands"},
            {197, "MinimapCrystalLeague"},
            {198, "MinimapCorruptedDungeon"},
            {199, "MinimapMists"},
            {200, "MinimapSiegeCamp"},
            {201, "MinimapTerritory"},
            {202, "MinimapWatchtower"},
            {203, "MinimapHideout"},
            {204, "MinimapOpenWorldChest"},
            {205, "MinimapTutorialBlocker"},
            {206, "MinimapDynamicEvent"},
            {207, "MinimapRoad"},
            {208, "MinimapRoadExit"},
            {211, "NewHellgateExitObject"},
            {212, "NewExpeditionStart"},
            {213, "NewExpeditionReturn"},
            {214, "NewArenaStart"},
            {215, "NewArenaReturn"},
            {216, "NewHellgateStart"},
            {217, "NewHellgateReturn"},
            {218, "NewOutlandsStart"},
            {219, "NewOutlandsReturn"},
            {220, "NewCrystalLeagueStart"},
            {221, "NewCrystalLeagueReturn"},
            {222, "NewCorruptedDungeonStart"},
            {223, "NewCorruptedDungeonReturn"},
            {224, "NewMistsStart"},
            {225, "NewMistsReturn"},
            {226, "NewTutorialStart"},
            {227, "NewTutorialReturn"},
            {228, "NewDynamicEventStart"},
            {229, "NewDynamicEventReturn"},
            {230, "NewRoadStart"},
            {231, "NewRoadReturn"},
            {232, "UpdateAuction"},
            {233, "UpdateMarket"},
            {234, "UpdateVault"},
            {235, "UpdateInventory"},
            {236, "UpdatePlayerStats"},
            {237, "UpdatePlayerSkills"},
            {238, "UpdatePlayerDestiny"},
            {239, "UpdatePlayerCrafting"},
            {240, "UpdatePlayerGathering"},
            {241, "UpdatePlayerFarming"},
            {242, "UpdatePlayerFishing"},
            {243, "UpdatePlayerMercenary"},
            {244, "UpdatePlayerAchievements"},
            {245, "UpdatePlayerTutorial"},
            {246, "UpdatePlayerConquerorChallenge"},
            {247, "UpdatePlayerDailyChallenge"},
            {248, "UpdatePlayerWeeklyChallenge"},
            {249, "UpdatePlayerSeasonChallenge"},
            {250, "UpdatePlayerEventChallenge"},
            {251, "UpdatePlayerActivityLog"},
            {252, "UpdatePlayerSocial"},
            {253, "UpdatePlayerSettings"},
            {254, "UpdatePlayerMail"},
            {255, "Invalidate"}, // Ini code terakhir yang diketahui dari sumber, bisa ada lebih
        };
        
        public PhotonPacketParser(ILogger<PhotonPacketParser> logger = null)
        {
            _logger = logger;
            _protocol16 = new Protocol16();
        }

        public PhotonEvent ParseMessage(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                _logger?.LogWarning("Received null or empty packet data for message parsing.");
                return null;
            }

            try
            {
                // DeserializeMessage akan menangani seluruh paket Photon termasuk headernya.
                // Ini mengembalikan sebuah MessageBase, yang bisa berupa EventData, OperationResponse, dll.
                MessageBase message = _protocol16.DeserializeMessage(new MemoryStream(data));

                if (message is EventData eventData)
                {
                    _logger?.LogTrace("Parsed EventData. Code: {EventCode}, Parameters Count: {ParameterCount}", eventData.Code, eventData.Parameters?.Count);
                    return new PhotonEvent
                    {
                        Code = eventData.Code,
                        Parameters = eventData.Parameters
                    };
                }
                else if (message is OperationResponse operationResponse)
                {
                    // Biasanya kita lebih tertarik pada EventData untuk scanner.
                    // Tapi bisa saja OperationResponse juga mengandung data yang relevan.
                    _logger?.LogTrace("Parsed OperationResponse. OperationCode: {OpCode}, ReturnCode: {ReturnCode}, DebugMessage: {DebugMsg}", 
                                    operationResponse.OperationCode, operationResponse.ReturnCode, operationResponse.DebugMessage);
                    // Anda bisa memutuskan untuk mengembalikan ini juga atau mengabaikannya
                    // return new PhotonEvent { Code = operationResponse.OperationCode, Parameters = operationResponse.Parameters, IsOperationResponse = true, ReturnCode = operationResponse.ReturnCode };
                    return null; // Contoh: abaikan OperationResponse untuk scanner ini
                }
                else if (message is OperationRequest operationRequest)
                {
                     _logger?.LogTrace("Parsed OperationRequest (client to server). OperationCode: {OpCode}", operationRequest.OperationCode);
                    return null; // Biasanya diabaikan untuk scanner sisi client
                }
                else
                {
                    _logger?.LogWarning("Parsed unknown Photon message type: {MessageType}", message?.GetType().Name ?? "null");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception during Photon message deserialization. Data length: {DataLength}", data.Length);
            }
            return null;
        }


        public string GetEventName(byte code)
        {
            return _eventCodes.TryGetValue(code, out var name) ? name : $"UnknownEvent_{code}";
        }
         public string GetOperationName(byte code)
        {
            // Anda perlu mengisi _operationCodes dari sumber seperti AlbionTracker
            // https://github.com/0blu/AlbionTracker/blob/master/Albion.Common/Photon/OperationCodes.cs
            // Contoh:
            // { 253, "Move" },
            // { 2, "Join" },
            // ...
            return _operationCodes.TryGetValue(code, out var name) ? name : $"UnknownOp_{code}";
        }
    }
}