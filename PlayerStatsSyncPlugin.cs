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
using System.Text.RegularExpressions;

namespace PlayerStatsSync;


// TODO
// Log FPS to file
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
        // Server does not receive this event! Figure out another way to get UpdateStats running on server
        FikaEventDispatcher.SubscribeEvent<GameWorldStartedEvent>(OnGameWorldStartedEvent);

        FikaEventDispatcher.SubscribeEvent<FikaServerCreatedEvent>(OnServerCreatedEvent);
        FikaEventDispatcher.SubscribeEvent<FikaServerDestroyedEvent>(OnServerDestroyedEvent);
    }

    private void OnDisable()
    {
        FikaEventDispatcher.UnsubscribeEvent<FikaClientCreatedEvent>(OnClientCreatedEvent);
        FikaEventDispatcher.UnsubscribeEvent<FikaClientDestroyedEvent>(OnClientDestroyedEvent);
        FikaEventDispatcher.UnsubscribeEvent<GameWorldStartedEvent>(OnGameWorldStartedEvent);

        FikaEventDispatcher.UnsubscribeEvent<FikaServerCreatedEvent>(OnServerCreatedEvent);
        FikaEventDispatcher.UnsubscribeEvent<FikaServerDestroyedEvent>(OnServerDestroyedEvent);
        CancelUpdateStats();
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
        InvokeRepeating("UpdateMyStats", 1f, interval);
        InvokeRepeating("SendPlayerStatsUpdate", 1f, interval);
    }

    private void CancelUpdateStats() {
        CancelInvoke("UpdateMyStats");
        CancelInvoke("SendPlayerStatsUpdate");
    }

    private void UpdateMyStats() {
        if (!Singleton<GameWorld>.Instantiated) {
            return;
        }
        Logger.LogInfo("GETTING MAIN PLAYER");
        CoopPlayer myPlayer = (CoopPlayer)Singleton<GameWorld>.Instance.MainPlayer;
        Logger.LogInfo($"MYPLAYER PROFILE {myPlayer.ProfileId}");
        Logger.LogInfo($"MYPLAYER Nickname {myPlayer.Profile.Nickname}");
        Logger.LogInfo($"MYPLAYER isdedi {MatchmakerAcceptPatches.IsDedicated}");
        Logger.LogInfo($"MYPLAYER isServer {MatchmakerAcceptPatches.IsServer}");
        playerInfoMap[myPlayer.ProfileId] = new PlayerInfo{
            FPS=(int) fps,
            ProfileID=myPlayer.ProfileId,
            Nickname=myPlayer.Profile.Nickname,
            IsDedi=MatchmakerAcceptPatches.IsDedicated,
            IsServer=MatchmakerAcceptPatches.IsServer,
        };
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
        packet.IsDedi = MatchmakerAcceptPatches.IsDedicated;
        packet.IsServer = MatchmakerAcceptPatches.IsServer;

        if (MatchmakerAcceptPatches.IsClient) {
            Logger.LogInfo($"Sending packet to server: {packet.Timestamp} {packet.Nickname} {packet.ProfileID} {packet.FPS}");
            writer.Reset();
            Singleton<FikaClient>.Instance.SendData(writer, ref packet, DeliveryMethod.Unreliable);
        }
        // Send server's own stats to all clients
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
        GUIStyle clearStyle = new () { normal = { textColor = Color.clear }};
        GUILayout.BeginArea(new Rect(screenX - uiWidth, 5, uiWidth, uiHeight));
        GUILayout.BeginVertical(drawBox ? "box" : "");
        foreach(var player in playerInfoMap.Values){
            int nameLength = player.Nickname.Length;
            GUILayout.BeginHorizontal();
            GUILayout.Label("D", player.IsDedi ? normalStyle : clearStyle);
            GUILayout.Label("S", player.IsServer ? normalStyle : clearStyle);
            GUILayout.Space(5);
            GUILayout.Label($"{player.Nickname}: ");
            //GUILayout.Label($"{(nameLength > 20 ? player.Nickname.Substring(0, 17) + "..." : player.Nickname)}: ", normalStyle);
            // Empty padding of consistent size
            //GUILayout.Label($"{new string('0', nameLength >= 20 ? 0 : 20 - nameLength)}", clearStyle);
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
        Logger.LogInfo("Starting update stats on client");
        UpdateStats();
        ev.Client.packetProcessor.SubscribeNetSerializable<PlayerStatsPacket>(processPlayerStatsPacket);
    }

    private void OnClientDestroyedEvent(FikaClientDestroyedEvent ev)
    {
        Logger.LogInfo("Cancelling update stats due to client destroy");
        ev.Client.packetProcessor.RemoveSubscription<PlayerStatsPacket>();
        CancelUpdateStats();
    }

    private void OnServerCreatedEvent(FikaServerCreatedEvent ev)
    {
        Logger.LogInfo("Starting update stats on server");
        UpdateStats();
        ev.Server.packetProcessor.SubscribeNetSerializable<PlayerStatsPacket>(processPlayerStatsPacket);
    }

    private void OnServerDestroyedEvent(FikaServerDestroyedEvent ev)
    {
        Logger.LogInfo("Cancelling update stats due to server destroy");
        ev.Server.packetProcessor.RemoveSubscription<PlayerStatsPacket>();
        CancelUpdateStats();
    }
    
    private void SavePlayerInfo(PlayerStatsPacket packet)
    {
        playerInfoMap[packet.ProfileID] = new PlayerInfo
        {
            FPS = packet.FPS,
            ProfileID = packet.ProfileID,
            Nickname = packet.Nickname,
            IsDedi = packet.IsDedi,
            IsServer = packet.IsServer,
        };
    }

    private void processPlayerStatsPacket(PlayerStatsPacket packet)
    {
        Logger.LogInfo($"Received packet {packet.Timestamp} [{packet.Nickname}]: FPS {packet.FPS}");
        CoopPlayer myPlayer = (CoopPlayer)Singleton<GameWorld>.Instance.MainPlayer;
        if (packet.ProfileID != myPlayer.ProfileId){
            SavePlayerInfo(packet);
        }
        if (MatchmakerAcceptPatches.IsServer) {
            // Propagate to all clients
            var broadcastPacket = new PlayerStatsPacket { ProfileID=packet.ProfileID, Nickname=packet.Nickname, FPS=packet.FPS, Timestamp=packet.Timestamp};
            Logger.LogInfo($"Propagating {broadcastPacket.Timestamp} {broadcastPacket.Nickname} packet to all clients");
            writer.Reset();
            Singleton<FikaServer>.Instance.SendDataToAll(writer, ref broadcastPacket, DeliveryMethod.Unreliable);
        }
    }

    // Client only
    private void OnGameWorldStartedEvent(GameWorldStartedEvent ev){
        Logger.LogInfo("GAMEWORLD STARTED");
    }
}