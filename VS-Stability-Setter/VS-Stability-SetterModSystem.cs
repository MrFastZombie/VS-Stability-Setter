using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using ProtoBuf;
using Vintagestory.API.Util;
using System.Collections.Generic;
using HarmonyLib;
using System;
using Vintagestory.API.MathTools;
using System.ComponentModel;

namespace VS_Stability_Setter;

public class VS_Stability_SetterModSystem : ModSystem
{
     private static ICoreServerAPI? ServerAPI { get; set; }
     
     private static Dictionary<string, float>  setChunks = new();
    private static int confirmCode = 0;
     private static int StabilityMode = 0;
     private static float GlobalStability = 1;
     private static float GlobalStabilityOffset = 0;

     private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) {
        return side == EnumAppSide.Server;
    }

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
        public ServerChunkPos(BlockPos pos) {
            const int chunkSize = GlobalConstants.ChunkSize;
            X = pos.X / chunkSize;
            Y = pos.Y / chunkSize;
            Z = pos.Z / chunkSize;
        }

        public ServerChunkPos(string pos) {
            var split = pos.Split(',');
            X = int.Parse(split[0]);
            Y = int.Parse(split[1]);
            Z = int.Parse(split[2]);
        }
        public override string ToString() => $"{X},{Y},{Z}";
    }
    public override void StartServerSide(ICoreServerAPI api) {
        base.StartServerSide(api);
        ServerAPI = api;

        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();

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
        
        api.Event.SaveGameLoaded += OnSaveGameLoading;
        api.Event.GameWorldSave += OnSaveGameSaving;
    }

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

    #region Commands
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

        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);

        if( setChunks.ContainsKey(chunkPos.ToString()) ) {
            setChunks.Remove(chunkPos.ToString());
        }
        
        return TextCommandResult.Success();
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

        return TextCommandResult.Success(Lang.Get("vsstabilitysetter:setstabmode-success", StabilityModeString));
    }
    #endregion

    /// <summary>
    /// Harmony patch that overrides the stability value return of GetTemporalStability(). The vanilla behavior actually seems to use the generation algorithm whenever it is called, and does not store the value anywhere so the getter has to be patched.
    /// </summary>
    [HarmonyPatch(typeof(Vintagestory.GameContent.SystemTemporalStability), "GetTemporalStability", new Type[] {typeof(double), typeof(double), typeof(double)})]
    public class TemporalStabilityPatch {
        public static void Postfix(Vintagestory.GameContent.SystemTemporalStability __instance, ref float __result, ref double x, ref double y, ref double z) {
            if(ServerAPI != null) {
                bool stabilityEnabled = (bool)ServerAPI.World.Config["temporalStability"].GetValue();
                if(stabilityEnabled) { //Only change anything if stability is enabled.
                    BlockPos pos = new((int)x, (int)y, (int)z);
                    ServerChunkPos chunkPos = new(pos);
                    if(setChunks != null && StabilityMode == 0) { //Vanilla behavior mode
                        if(setChunks.ContainsKey(chunkPos.ToString())) { //Only change if the chunk has been set.
                            __result = setChunks[chunkPos.ToString()];
                        }
                    }
                    if(StabilityMode == 1) { //Global stability mode
                        __result = GlobalStability;
                    }
                    if(StabilityMode == 2) { //Global stability offset mode
                        if(setChunks != null) {
                            if(setChunks.ContainsKey(chunkPos.ToString())) {
                                __result = setChunks[chunkPos.ToString()];
                            }
                        }
                        __result += GlobalStabilityOffset;
                        __result = Math.Clamp(__result, -10000, 10000); //Don't let it go out of bounds!
                    }
                }
            }
        }
    }

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
