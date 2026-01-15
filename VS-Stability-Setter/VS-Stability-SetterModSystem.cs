using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using ProtoBuf;
using Vintagestory.API.Util;
using System.Collections.Generic;
using HarmonyLib;
using System;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;

namespace VS_Stability_Setter;

public class VS_Stability_SetterModSystem : ModSystem
{
    /// <summary>
    /// Used to store the chunk position, and convert a blockpos to a usable chunk position.
    /// </summary>
    [ProtoContract]
    public class ServerChunkPos {
        [ProtoMember(1)]
        public int X { get; set; }
        [ProtoMember(2)]
        public int Y { get; set; }
        [ProtoMember(3)]
        public int Z { get; set; }

        public int BlockX {get; set;}
        public int BlockY {get; set;}
        public int BlockZ {get; set;}
        public ServerChunkPos(BlockPos pos) {
            const int chunkSize = GlobalConstants.ChunkSize;
            X = pos.X / chunkSize;
            Y = pos.Y / chunkSize;
            Z = pos.Z / chunkSize;

            BlockX = pos.X;
            BlockY = pos.Y;
            BlockZ = pos.Z;
        }

        public ServerChunkPos(string pos) {
            var split = pos.Split(',');
            X = int.Parse(split[0]);
            Y = int.Parse(split[1]);
            Z = int.Parse(split[2]);

            BlockX = int.Parse(split[0]) * GlobalConstants.ChunkSize;
            BlockY = int.Parse(split[1]) * GlobalConstants.ChunkSize;
            BlockZ = int.Parse(split[2]) * GlobalConstants.ChunkSize;
        }
        public override string ToString() => $"{X},{Y},{Z}";
    }

    #region Server
     private static ICoreServerAPI? ServerAPI { get; set; }
     
     private static Dictionary<string, float>  setChunks = new();
    private static int confirmCode = 0;
    private static int StabilityMode = 0;
    private static float GlobalStability = 1;
    private static float GlobalStabilityOffset = 0;
     
    private static Network? serverNetwork;

    private Harmony? harmony;

    public override void StartServerSide(ICoreServerAPI api) {
        base.StartServerSide(api);
        ServerAPI = api;

        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();

        serverNetwork = new Network(api);

        api.ChatCommands.Create("setStability")
            .WithDescription(Lang.Get("vsstabilitysetter:setstab-desc"))
            .RequiresPrivilege(Privilege.ban)
            .RequiresPlayer()
            .WithArgs(ServerAPI.ChatCommands.Parsers.DoubleRange("stability", -10000, 10000))
            .HandleWith(new OnCommandDelegate(OnSetStabCommand));
        api.ChatCommands.Create("resetStability")
            .WithDescription(Lang.Get("vsstabilitysetter:resetstab-desc"))
            .RequiresPrivilege(Privilege.ban)
            .RequiresPlayer()
            .HandleWith(new OnCommandDelegate(OnResetStabCommand))
            .BeginSubCommand("all")
                .WithDescription(Lang.Get("vsstabilitysetter:resetstab-all-desc"))
                .RequiresPrivilege(Privilege.ban)
                .WithArgs(ServerAPI.ChatCommands.Parsers.OptionalIntRange("confirmCode", 10000, 99999, -1))
                .HandleWith((args) => {
                    int inputCode = args.LastArg == null ? -1 : Convert.ToInt32(args.LastArg.ToString());
                    if(inputCode == confirmCode && confirmCode != -1) {
                        setChunks.Clear();
                        confirmCode = -1;
                        serverNetwork?.BroadcastData(setChunks, StabilityMode, GlobalStability, GlobalStabilityOffset, null);
                        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:resetstab-all-success"));
                    } else if(inputCode == -1) {
                        int randomCode = new Random().Next(10000, 99999);
                        confirmCode = randomCode;
                        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:resetstab-all-warning", confirmCode));
                    } else {
                        if(confirmCode == -1) return TextCommandResult.Error(Lang.Get("vsstabilitysetter:resetstab-all-error2"));
                        return TextCommandResult.Error(Lang.Get("vsstabilitysetter:resetstab-all-error", confirmCode));
                    }
                })
            .EndSubCommand();
        api.ChatCommands.Create("getStability")
            .WithDescription(Lang.Get("vsstabilitysetter:getstab-desc"))
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .HandleWith(new OnCommandDelegate(OnGetStabCommand))
            .BeginSubCommand("global")
                .WithDescription(Lang.Get("vsstabilitysetter:get-stabglobal-desc"))
                .RequiresPrivilege(Privilege.chat)
                .HandleWith((args) => {
                    return TextCommandResult.Success(Lang.Get("vsstabilitysetter:get-globalstab-success", GlobalStability));
                })
            .EndSubCommand()
            .BeginSubCommand("offset")
                .WithDescription(Lang.Get("vsstabilitysetter:get-stabglobaloffset-desc"))
                .RequiresPrivilege(Privilege.chat)
                .HandleWith((args) => {
                    return TextCommandResult.Success(Lang.Get("vsstabilitysetter:get-globaloffset-success", GlobalStabilityOffset));
                })
            .EndSubCommand()
            .BeginSubCommand("mode")
                .WithDescription(Lang.Get("vsstabilitysetter:get-mode-desc"))
                .RequiresPrivilege(Privilege.chat)
                .HandleWith((args) => {
                    return TextCommandResult.Success(Lang.Get("vsstabilitysetter:get-mode-success", GetModeString(StabilityMode)));
                })
            .EndSubCommand();
        api.ChatCommands.Create("setStabilityMode")
            .WithDescription(Lang.Get("vsstabilitysetter:setstabmode-desc"))
            .RequiresPrivilege(Privilege.ban)
            .HandleWith(new OnCommandDelegate(OnSetStabModeCommand))
            .WithArgs(ServerAPI.ChatCommands.Parsers.IntRange("mode", 0, 2));
        api.ChatCommands.Create("setGlobalStability")
            .WithDescription(Lang.Get("vsstabilitysetter:setglobalstab-desc"))
            .WithArgs(ServerAPI.ChatCommands.Parsers.DoubleRange("stability", -10000, 10000))
            .RequiresPrivilege(Privilege.ban)
            .HandleWith(new OnCommandDelegate(OnSetGlobalStabCommand));
        api.ChatCommands.Create("setGlobalStabilityOffset")
            .WithDescription(Lang.Get("vsstabilitysetter:setglobalstaboffset-desc"))
            .WithArgs(ServerAPI.ChatCommands.Parsers.DoubleRange("stability", -10000, 10000))
            .RequiresPrivilege(Privilege.ban)
            .HandleWith(new OnCommandDelegate(OnSetGlobalStabOffsetCommand));
        api.ChatCommands.Create("requestStabilityData")
            .WithDescription(Lang.Get("vsstabilitysetter:requeststabdata-desc"))
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .HandleWith((args) => {
                if(serverNetwork == null) return TextCommandResult.Error(Lang.Get("vsstabilitysetter:no-network"));
                serverNetwork.BroadcastData(setChunks, StabilityMode, GlobalStability, GlobalStabilityOffset, args.Caller.Player as IServerPlayer);
                return TextCommandResult.Success(Lang.Get("vsstabilitysetter:requeststabdata-success"));
            });
        
        api.Event.SaveGameLoaded += OnSaveGameLoading;
        api.Event.GameWorldSave += OnSaveGameSaving;
        api.Event.PlayerNowPlaying += Event_PlayerJoin;
    }

    private void Event_PlayerJoin(IServerPlayer player)
    {
        //This updates a player's data when they join.
        if(serverNetwork == null) return;
        serverNetwork.BroadcastData(setChunks, StabilityMode, GlobalStability, GlobalStabilityOffset, player);
    }

    #endregion

    #region Client

    private static ICoreClientAPI? ClientAPI { get; set; }
    private static IClientNetworkChannel? clientChannel;
    private static Dictionary<string, float> clientSetChunks = new();
    private static int ClientStabilityMode = 0;
    private static float ClientGlobalStability = 1;
    private static float ClientGlobalStabilityOffset = 0;

    public override void StartClientSide(ICoreClientAPI api) {
        base.StartClientSide(api);
        ClientAPI = api;

        clientChannel = api.Network.RegisterChannel("stabilitysetter")
            .RegisterMessageType(typeof(Network.DataSyncPacket))
            .RegisterMessageType(typeof(Network.UpdatePacket))
            .SetMessageHandler<Network.UpdatePacket>(OnUpdatePacket)
            .SetMessageHandler<Network.DataSyncPacket>(OnDataSyncPacket);

        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();
    }

    private void OnDataSyncPacket(Network.DataSyncPacket packet) {
        if(packet.Response.StartsWith("data-sync")) { //Data Sync
            clientSetChunks = packet.SendChunks == null ? new() : packet.SendChunks; //Ternary operator: val = condition ? true : false
            ClientStabilityMode = packet.SendStabilityMode;
            ClientGlobalStability = packet.SendGlobalStability;
            ClientGlobalStabilityOffset = packet.SendGlobalStabilityOffset;
        }
    }

    private void OnUpdatePacket(Network.UpdatePacket packet) {
        if(packet.Response.StartsWith("update")) { //Updates to chunk cache (To avoid resending all data)
            if(packet.SendChunkPosString == null) return; 
            ServerChunkPos chunkPos = new(packet.SendChunkPosString);
            float stability = packet.SendStability;
            bool remove = packet.SendRemove;
            if(remove) {
                clientSetChunks.Remove(chunkPos.ToString());
            } else {
                clientSetChunks[chunkPos.ToString()] = stability;
            }
        }
    }

    #endregion

    /// <summary>
    /// Converts the mode integer value to a string.
    /// </summary>
    /// <param name="mode"></param>
    /// <returns></returns>
    private string GetModeString(int mode) {
        string StabilityModeString = "";
        switch(mode) {
            case 0:
                StabilityModeString = Lang.Get("vsstabilitysetter:stab-mode-0");
                break;
            case 1:
                StabilityModeString = Lang.Get("vsstabilitysetter:stab-mode-1");
                break;
            case 2:
                StabilityModeString = Lang.Get("vsstabilitysetter:stab-mode-2");
                break;
        }
        return StabilityModeString;
    }

    #region Command implementations
    /// <summary>
    /// Command handler for setting the stability value of a chunk. 
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private TextCommandResult OnSetStabCommand(TextCommandCallingArgs args) {
        IServerPlayer player = (IServerPlayer) args.Caller.Player;
        
        if (player == null) { return TextCommandResult.Error(Lang.Get("vsstabilitysetter:not-player")); }
        if (ServerAPI == null) { return TextCommandResult.Error(Lang.Get("vsstabilitysetter:no-api")); }

        float stability = args.LastArg == null ? 1 : args.LastArg.ToString().ToFloat(); //Gets the stability from arguments. Defaults to 1 if someehow null.

        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);
        setChunks[chunkPos.ToString()] = stability;

        serverNetwork?.BroadcastChunkUpdate(chunkPos.ToString(), stability, false);

        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:setstab-success", chunkPos.ToString(), stability));
    }

    /// <summary>
    /// Command handler for resetting the stability value of a chunk.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private TextCommandResult OnResetStabCommand(TextCommandCallingArgs args) {
        IServerPlayer player = (IServerPlayer) args.Caller.Player;

        if (player == null) { return TextCommandResult.Error(Lang.Get("vsstabilitysetter:not-player")); }
        if (ServerAPI == null) { return TextCommandResult.Error(Lang.Get("vsstabilitysetter:no-api")); }

        Vintagestory.GameContent.SystemTemporalStability StabSystem = ServerAPI.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>(); //Declare a temporal stability system to later get the stability value from a BlockPos.
        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);

        if( setChunks.ContainsKey(chunkPos.ToString()) ) {
            setChunks.Remove(chunkPos.ToString());
        }

        serverNetwork?.BroadcastChunkUpdate(chunkPos.ToString(), 1, true);
        
        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:resetstab-success", chunkPos.ToString(), StabSystem.GetTemporalStability(player.Entity.Pos.AsBlockPos)));
    }

    /// <summary>
    /// Command handler for getting the stability value of a chunk.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private TextCommandResult OnGetStabCommand(TextCommandCallingArgs args) {
        IServerPlayer player = (IServerPlayer) args.Caller.Player;
        
        if (player == null) { return TextCommandResult.Error(Lang.Get("vsstabilitysetter:not-player")); }
        if (ServerAPI == null) { return TextCommandResult.Error(Lang.Get("vsstabilitysetter:no-api")); }

        Vintagestory.GameContent.SystemTemporalStability StabSystem = ServerAPI.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>(); //Declare a temporal stability system to later get the stability value from a BlockPos.
        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);
        float stability = StabSystem.GetTemporalStability(player.Entity.Pos.AsBlockPos);

        string extraInfo = "";

        switch(StabilityMode) {
            case 0: //Vanilla behavior mode
                break;
            case 1: //Global stability mode
                extraInfo = Lang.Get("vsstabilitysetter:extrainfo-global");
                break;
            case 2: //Global stability offset mode
                extraInfo = Lang.Get("vsstabilitysetter:extrainfo-offset", GlobalStabilityOffset.ToString());
                break;
        } 

        if(StabSystem != null) {
            return TextCommandResult.Success(Lang.Get("vsstabilitysetter:get-output", chunkPos.ToString(), stability.ToString(), extraInfo));
        }
        return TextCommandResult.Error(Lang.Get("vsstabilitysetter:get-error"));
    }

    /// <summary>
    /// Command handler for setting the global stability offset value.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private TextCommandResult OnSetGlobalStabOffsetCommand(TextCommandCallingArgs args) {
        float stability = args.LastArg == null ? 1 : args.LastArg.ToString().ToFloat();
        GlobalStabilityOffset = stability;
        serverNetwork?.BroadcastData(setChunks, StabilityMode, GlobalStability, GlobalStabilityOffset, null);
        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:setglobalstaboffset-success", GlobalStabilityOffset));
    }

    /// <summary>
    /// Command handler for setting the global stability value.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private TextCommandResult OnSetGlobalStabCommand(TextCommandCallingArgs args) {
        float stability = args.LastArg == null ? 1 : args.LastArg.ToString().ToFloat();
        GlobalStability = stability;
        serverNetwork?.BroadcastData(setChunks, StabilityMode, GlobalStability, GlobalStabilityOffset, null);
        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:setglobalstab-success", GlobalStability));
    }

    /// <summary>
    /// Command handler for setting the stability mode.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    private TextCommandResult OnSetStabModeCommand(TextCommandCallingArgs args) {
        int mode = args.LastArg == null ? 0 : Convert.ToInt32(args.LastArg.ToString());
        StabilityMode = mode;

        string StabilityModeString = GetModeString(StabilityMode);
        serverNetwork?.BroadcastData(setChunks, StabilityMode, GlobalStability, GlobalStabilityOffset, null);

        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:setstabmode-success", StabilityModeString));
    }
    #endregion

    #region Harmony patch
    /// <summary>
    /// Harmony patch that overrides the stability value return of GetTemporalStability(). The vanilla behavior actually seems to use the generation algorithm whenever it is called, and does not store the value anywhere so the getter has to be patched.
    /// </summary>
    [HarmonyPatch(typeof(Vintagestory.GameContent.SystemTemporalStability), "GetTemporalStability", new Type[] {typeof(double), typeof(double), typeof(double)})]
    public class TemporalStabilityPatch {
        public static void Postfix(Vintagestory.GameContent.SystemTemporalStability __instance, ref float __result, ref double x, ref double y, ref double z) {
            __result = StabilityPatch(__result, x, y, z);
        }
    }

    /// <summary>
    /// Harmony patch. Under most circumstances, it'll return the normal stability value for a chunk. If a stability mode is enabled or if the chunk has a custom value specified it will return a modified value.
    /// </summary>
    /// <param name="result"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public static float StabilityPatch(float result, double x, double y, double z) {
        if(ServerAPI != null) { //SERVER SIDE
            bool stabilityEnabled = (bool)ServerAPI.World.Config["temporalStability"].GetValue();
            if(stabilityEnabled) { //Only change anything if stability is enabled.
                Vintagestory.GameContent.SystemTemporalStability StabSystem = ServerAPI.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>(); //Declare a temporal stability system
                float stormMod = StabSystem.StormStrength + StabSystem.modGlitchStrength;
                bool stormActive = StabSystem.StormData.nowStormActive;
                BlockPos pos = new((int)x, (int)y, (int)z);
                ServerChunkPos chunkPos = new(pos);

                if(setChunks != null && StabilityMode == 0) { //Vanilla behavior mode
                    if(setChunks.ContainsKey(chunkPos.ToString())) { //Only change if the chunk has been set.
                        result = setChunks[chunkPos.ToString()];
                    }
                }
                if(StabilityMode == 1) { //Global stability mode
                    result = GlobalStability;
                }
                if(StabilityMode == 2) { //Global stability offset mode
                    if(setChunks != null) {
                        if(setChunks.ContainsKey(chunkPos.ToString())) {
                            result = setChunks[chunkPos.ToString()];
                        }
                    }
                    result += GlobalStabilityOffset;
                    result = Math.Clamp(result, -10000, 10000); //Don't let it go out of bounds!
                }
                if(stormActive) {
                    result = GameMath.Clamp(result - stormMod, -10000f, 1.5f);
                }
            }
        } else if(ClientAPI != null && clientChannel != null && clientChannel.Connected) { //CLIENT SIDE
            bool stabilityEnabled = (bool)ClientAPI.World.Config["temporalStability"].GetValue();
            if(stabilityEnabled) {
                Vintagestory.GameContent.SystemTemporalStability StabSystem = ClientAPI.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>(); //Declare a temporal stability system
                float stormMod = StabSystem.StormStrength + StabSystem.modGlitchStrength;
                bool stormActive = StabSystem.StormData.nowStormActive;
                BlockPos pos = new((int)x, (int)y, (int)z);
                ServerChunkPos chunkPos = new(pos);
                if(clientSetChunks != null && ClientStabilityMode == 0) { //Vanilla behavior mode
                    if(clientSetChunks.ContainsKey(chunkPos.ToString())) { //Only change if the chunk has been set.
                        result = clientSetChunks[chunkPos.ToString()];
                    }
                }
                if(ClientStabilityMode == 1) { //Global stability mode
                    result = ClientGlobalStability;
                }
                if(ClientStabilityMode == 2) { //Global stability offset mode
                    if(clientSetChunks != null) {
                        if(clientSetChunks.ContainsKey(chunkPos.ToString())) {
                            result = clientSetChunks[chunkPos.ToString()];
                        }
                    }
                    result += ClientGlobalStabilityOffset;
                    result = Math.Clamp(result, -10000, 10000); //Don't let it go out of bounds!
                }
                if(stormActive) {
                    result = GameMath.Clamp(result - stormMod, -10000f, 1.5f);
                }
            }
        }
        return result;
    }

    #endregion

    #region Data serialization

    /// <summary>
    /// Override the SaveGameSaving method to save the chunk stability data.
    /// </summary>
    private void OnSaveGameSaving() {
        if(ServerAPI == null) {
            return;
        }

        if(setChunks != null) {
            byte[] serializedDictionary = SerializerUtil.Serialize<Dictionary<string, float>>(setChunks); //I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF 
            ServerAPI.WorldManager.SaveGame.StoreData("setStabilityChunks", serializedDictionary);
        }

        byte[] serializedMode = SerializerUtil.Serialize<int>(StabilityMode);
        byte[] serializedGlobalStab = SerializerUtil.Serialize<float>(GlobalStability);
        byte[] serializedGlobalStabOffset = SerializerUtil.Serialize<float>(GlobalStabilityOffset);
        
        ServerAPI.WorldManager.SaveGame.StoreData("setStabilityMode", serializedMode);
        ServerAPI.WorldManager.SaveGame.StoreData("setStabilityGlobalStab", serializedGlobalStab);
        ServerAPI.WorldManager.SaveGame.StoreData("setStabilityGlobalStabOffset", serializedGlobalStabOffset);
        ServerAPI.Logger.Debug("Saved chunk stability data");
    }

    /// <summary>
    /// Override the SaveGameLoading method to load the chunk stability data.
    /// </summary>
    private void OnSaveGameLoading() {
        if(ServerAPI == null) {
            return;
        }

        byte[] chunkdata = ServerAPI.WorldManager.SaveGame.GetData("setStabilityChunks");
        byte[] modedata = ServerAPI.WorldManager.SaveGame.GetData("setStabilityMode");
        byte[] globalStabData = ServerAPI.WorldManager.SaveGame.GetData("setStabilityGlobalStab");
        byte[] globalStabOffsetData = ServerAPI.WorldManager.SaveGame.GetData("setStabilityGlobalStabOffset");

        setChunks = chunkdata == null ? new() : SerializerUtil.Deserialize<Dictionary<string, float>>(chunkdata);
        StabilityMode = modedata == null ? 0 : SerializerUtil.Deserialize<int>(modedata);
        GlobalStability = globalStabData == null ? 1 : SerializerUtil.Deserialize<float>(globalStabData);
        GlobalStabilityOffset = globalStabOffsetData == null ? 0 : SerializerUtil.Deserialize<float>(globalStabOffsetData);

        ServerAPI.Logger.Debug("Loaded chunk stability data");
    }
    #endregion

    /// <summary>
    /// Unpatch the vanilla method before disposing the mod.
    /// </summary>
    public override void Dispose() {
        harmony?.UnpatchAll(Mod.Info.ModID);
        base.Dispose();
    }
}
