using BepInEx;
using BepInEx.Logging;
using LiteNetLib;
using LiteNetLib.Utils;

using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Modding;
using Fika.Core.Coop.Players;
using Comfort.Common;
using UnityEngine;
using EFT;
using System.Collections.Generic;
using Fika.Core.Coop.Matchmaker;

namespace PlayerStatsSync;

/* Setup steps
- Add References ItemGroup to csproj file, pointing to References folder including everything in the Tarkov Managed folder as well as Fika core plugin
- Copy EscapeFromTarkov_Data
*/

// TODO
// Log FPS to file
// Send FPS as data packet to clients to display host FPS stats
//   All clients on init will get list of players, then start sending data packet to server in intervals at max rate of 1s
//   Server propagates that info to all clients
//   Listener on clients will process that packet and display
// Opt out of sending FPS values
// ?? Warn of desync of bot counts between host/client?
[BepInPlugin("com.zhl.playerstatssync", "zhl.PlayerStatsSync", "0.1.0.0")]
[BepInDependency("com.fika.core", "0.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    private NetDataWriter writer;

    bool showGUI = true;
    int fpsWarnThreshold = 100;
    int fpsCriticalThreshold = 30;
    GUIStyle warnStyle = new() { normal = { textColor = Color.yellow }};
    GUIStyle criticalStyle = new() { normal = { textColor = Color.red }};
    GUIStyle normalStyle = new() { normal = { textColor = Color.white }};
    bool drawBox = true;

    // How often to update the server
    int interval = 1;
    float fpsUpdateTimer = 0.2f;
    float fps;
    Dictionary<string, PlayerInfo> playerInfoMap = new Dictionary<string, PlayerInfo>();

    private void Awake()
    {
        writer = new NetDataWriter();

        // Plugin startup logic
        Logger = base.Logger;
    }


    private void OnEnable()
    {
        showGUI = true;
        FikaEventDispatcher.SubscribeEvent<FikaClientCreatedEvent>(OnClientCreatedEvent);
        FikaEventDispatcher.SubscribeEvent<FikaClientDestroyedEvent>(OnClientDestroyedEvent);
        FikaEventDispatcher.SubscribeEvent<GameWorldStartedEvent>(OnGameWorldStartedEvent);

        FikaEventDispatcher.SubscribeEvent<FikaServerCreatedEvent>(OnServerCreatedEvent);
        FikaEventDispatcher.SubscribeEvent<FikaServerDestroyedEvent>(OnServerDestroyedEvent);
        InvokeRepeating("UpdateMyStats", 1f, interval);
        /*
        if (Singleton<GameWorld>.Instantiated){
            // Already in a game world, let's begin updates
            UpdateStats();
        }
        */
    }

    private void OnDisable()
    {
        FikaEventDispatcher.UnsubscribeEvent<FikaClientCreatedEvent>(OnClientCreatedEvent);
        FikaEventDispatcher.UnsubscribeEvent<FikaClientDestroyedEvent>(OnClientDestroyedEvent);
        FikaEventDispatcher.UnsubscribeEvent<GameWorldStartedEvent>(OnGameWorldStartedEvent);

        FikaEventDispatcher.UnsubscribeEvent<FikaServerCreatedEvent>(OnServerCreatedEvent);
        FikaEventDispatcher.UnsubscribeEvent<FikaServerDestroyedEvent>(OnServerDestroyedEvent);
    }

    private void Update()
    {
        UpdateFPS();
    }

    private void UpdateFPS()
    {
        fpsUpdateTimer -= Time.deltaTime;
        if (fpsUpdateTimer <= 0f) {
            fps = 1f/Time.unscaledDeltaTime;
            fpsUpdateTimer = 0.2f;
        }
    }

    private void UpdateStats() {
        Logger.LogInfo("INVOKING SendPlayerStatsUpdate");
        InvokeRepeating("SendPlayerStatsUpdate", 1f, interval);
    }

    private void UpdateMyStats() {
        Logger.LogInfo("Updating my stats");
        if (!Singleton<GameWorld>.Instantiated) {
            return;
        }
        CoopPlayer myPlayer = (CoopPlayer)Singleton<GameWorld>.Instance.MainPlayer;
        playerInfoMap[myPlayer.ProfileId] = new PlayerInfo{ FPS=(int) fps, ProfileID=myPlayer.ProfileId, Nickname=myPlayer.Profile.Nickname };
    }

    // 3.9 support
    // Change all references to use aki
    // Change isClient/isServer logic to use FikaBackendUtils
    private void SendPlayerStatsUpdate()
    {
        if (!Singleton<GameWorld>.Instantiated) {
            return;
        }
        var packet = new PlayerStatsPacket { };

        CoopPlayer myPlayer = (CoopPlayer)Singleton<GameWorld>.Instance.MainPlayer;
        packet.FPS = (int) fps;
        packet.ProfileID = myPlayer.ProfileId;
        packet.Nickname = myPlayer.Profile.Nickname;
        packet.Timestamp = $"{Time.unscaledTime}";

        if (MatchmakerAcceptPatches.IsClient) {
            Logger.LogInfo($"Sending packet to server: {packet.Timestamp} {packet.Nickname} {packet.ProfileID} {packet.FPS}");
            writer.Reset();
            Singleton<FikaClient>.Instance.SendData(writer, ref packet, DeliveryMethod.Unreliable);
        }
        // Send server's stats to all clients
        else if (MatchmakerAcceptPatches.IsServer) {
            Logger.LogInfo($"Sending server stats to all clients: {packet.Timestamp} {packet.Nickname} {packet.ProfileID} {packet.FPS}");
            writer.Reset();
            Singleton<FikaServer>.Instance.SendDataToAll(writer, ref packet, DeliveryMethod.Unreliable);
        }
    }

    void OnGUI()
    {
        if (!showGUI) {
            return;
        }
        if (!Singleton<GameWorld>.Instantiated){
            return;
        }
        if (false && playerInfoMap.Count == 0) {
            return;
        }
        var screenX = Screen.width;
        var screenY = Screen.height;
        var uiWidth = 200;
        var uiHeight = 200;
        GUIStyle normalStyle = new() { normal = { textColor = Color.white }};
        GUILayout.BeginArea(new Rect(screenX - uiWidth, 5, uiWidth, uiHeight));
        GUILayout.BeginVertical(drawBox ? "box" : "");
        foreach(var player in playerInfoMap.Values){
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{player.Nickname}: ", normalStyle);
            GUILayout.Label($"{player.FPS}", getFPSStyle(player.FPS));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private GUIStyle getFPSStyle(int FPS)
    {
        if (FPS < fpsCriticalThreshold) {
            return criticalStyle;
        } else if (FPS < fpsWarnThreshold) {
            return warnStyle;
        }
        return normalStyle;
    }

    private void OnClientCreatedEvent(FikaClientCreatedEvent ev)
    {
        ev.Client.packetProcessor.SubscribeNetSerializable<ServerUpdatePacket>(processServerUpdatePacket);
    }

    private void OnServerCreatedEvent(FikaServerCreatedEvent ev)
    {
        ev.Server.packetProcessor.SubscribeNetSerializable<PlayerStatsPacket>(processPlayerStatsPacket);
    }

    private void OnServerDestroyedEvent(FikaServerDestroyedEvent ev)
    {
        ev.Server.packetProcessor.RemoveSubscription<PlayerStatsPacket>();
    }

    private void OnClientDestroyedEvent(FikaClientDestroyedEvent ev)
    {
        ev.Client.packetProcessor.RemoveSubscription<ServerUpdatePacket>();
    }

    private void processPlayerStatsPacket(PlayerStatsPacket packet)
    {
        Logger.LogInfo($"Received packet {packet.Timestamp} [{packet.Nickname}]: FPS {packet.FPS}");
        playerInfoMap[packet.ProfileID] = new PlayerInfo { FPS=packet.FPS, ProfileID=packet.ProfileID, Nickname=packet.Nickname };
        if (MatchmakerAcceptPatches.IsServer) {
            // Propagate to all clients
            var broadcastPacket = new ServerUpdatePacket { ProfileID=packet.ProfileID, Nickname=packet.Nickname, FPS=packet.FPS, Timestamp=packet.Timestamp};
            Logger.LogInfo($"Propagating {broadcastPacket.Timestamp} {broadcastPacket.Nickname} packet to all clients");
            writer.Reset();
            Singleton<FikaServer>.Instance.SendDataToAll(writer, ref broadcastPacket, DeliveryMethod.Unreliable);
        }
    }

    private void processServerUpdatePacket(ServerUpdatePacket packet)
    {
        Logger.LogInfo($"Received server update packet {packet.Timestamp} [{packet.Nickname}]: FPS {packet.FPS}");
        CoopPlayer myPlayer = (CoopPlayer)Singleton<GameWorld>.Instance.MainPlayer;
        if (packet.ProfileID != myPlayer.ProfileId) {
            playerInfoMap[packet.ProfileID] = new PlayerInfo { FPS=packet.FPS, ProfileID=packet.ProfileID, Nickname=packet.Nickname };
        }
    }

    private void OnGameWorldStartedEvent(GameWorldStartedEvent ev){
        UpdateStats();
    }
}