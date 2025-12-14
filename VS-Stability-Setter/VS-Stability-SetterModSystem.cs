using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using System.Runtime.CompilerServices;
using Vintagestory.API.Config;
using ProtoBuf;
using Vintagestory.API.Util;
using System.Collections.Generic;
using HarmonyLib;
using System;
using Vintagestory.API.MathTools;
using Newtonsoft.Json;

namespace VS_Stability_Setter;

public class VS_Stability_SetterModSystem : ModSystem
{
     private static ICoreServerAPI? ServerAPI { get; set; }
     
     private static Dictionary<string, float>  setChunks = new();

     private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) {
        return side == EnumAppSide.Server;
    }

    [ProtoContract]
    public class StabilityChunkData {
        [ProtoMember(1)]
        public required IServerChunk Chunk { get; set; }
        [ProtoMember(2)]
        public float Stability { get; set; }
    }

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
            .WithDescription(Lang.Get("vs-stability-setter:setstab-desc"))
            .RequiresPrivilege(Privilege.ban)
            .RequiresPlayer()
            .WithArgs(ServerAPI.ChatCommands.Parsers.Float("stabilityAmount"))
            .HandleWith(new OnCommandDelegate(OnSetStabCommand));
        api.ChatCommands.Create("resetStability")
            .WithDescription(Lang.Get("vs-stability-setter:resetstab-desc"))
            .RequiresPrivilege(Privilege.ban)
            .RequiresPlayer()
            .HandleWith(new OnCommandDelegate(OnResetStabCommand));
        api.ChatCommands.Create("getStability")
            .WithDescription(Lang.Get("vs-stability-setter:getstab-desc"))
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .HandleWith(new OnCommandDelegate(OnGetStabCommand));
        
        api.Event.SaveGameLoaded += OnSaveGameLoading;
        api.Event.GameWorldSave += OnSaveGameSaving;
    }

    private TextCommandResult OnSetStabCommand(TextCommandCallingArgs args) {
        
        IServerPlayer player = (IServerPlayer) args.Caller.Player;
        
        if (player == null || ServerAPI == null) {
            return TextCommandResult.Error(Lang.Get("vs-stability-setter:not-player"));
        }

        float stability = args.LastArg == null ? 1 : args.LastArg.ToString().ToFloat();

        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);
        setChunks[chunkPos.ToString()] = stability;

        return TextCommandResult.Success();
    }

    private TextCommandResult OnResetStabCommand(TextCommandCallingArgs args) {
        IServerPlayer player = (IServerPlayer) args.Caller.Player;

        if (player == null || ServerAPI == null) {
            return TextCommandResult.Error(Lang.Get("vs-stability-setter:not-player"));
        }

        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);

        if( setChunks.ContainsKey(chunkPos.ToString()) ) {
            setChunks.Remove(chunkPos.ToString());
        }
        
        return TextCommandResult.Success();
    }

    private TextCommandResult OnGetStabCommand(TextCommandCallingArgs args) {
        
        IServerPlayer player = (IServerPlayer) args.Caller.Player;
        
        if (player == null || ServerAPI == null) {
            return TextCommandResult.Error(Lang.Get("vs-stability-setter:not-player"));
        }

        Vintagestory.GameContent.SystemTemporalStability StabSystem = ServerAPI.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>();
        ServerChunkPos chunkPos = new(player.Entity.Pos.AsBlockPos);

        if(setChunks.ContainsKey(chunkPos.ToString()) ) {
            return TextCommandResult.Success(setChunks[chunkPos.ToString()].ToString());
        } else {
            if(StabSystem != null) {
                float stability = StabSystem.GetTemporalStability(player.Entity.Pos.AsBlockPos);
                return TextCommandResult.Success(stability.ToString());
            }
            return TextCommandResult.Error("Couldn't get chunk stability"); //TODO: Make this use a translation key.
        }
    }

    [HarmonyPatch(typeof(Vintagestory.GameContent.SystemTemporalStability), "GetTemporalStability", new Type[] {typeof(double), typeof(double), typeof(double)})]
    public class TemporalStabilityPatch {
        public static void Postfix(Vintagestory.GameContent.SystemTemporalStability __instance, ref float __result, ref double x, ref double y, ref double z) {
            if(ServerAPI != null) {
                bool stabilityEnabled = (bool)ServerAPI.World.Config["temporalStability"].GetValue();
                if(stabilityEnabled) {
                    BlockPos pos = new((int)x, (int)y, (int)z);
                    ServerChunkPos chunkPos = new(pos);
                    if(setChunks != null && setChunks != null) {
                        if(setChunks.ContainsKey(chunkPos.ToString())) {
                            __result = setChunks[chunkPos.ToString()];
                        }
                    }
                }
            }
        }
    }
    private void OnSaveGameSaving() {
        if(setChunks == null || ServerAPI == null) {
            return;
        }
        byte[] serializedData = SerializerUtil.Serialize<Dictionary<string, float>>(setChunks); //I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF I HATE PROTOBUF 
        //byte[] serializedData = SerializerUtil.Serialize<Dictionary<IServerChunk, float>>(setChunks);
        ServerAPI.WorldManager.SaveGame.StoreData("setStabilityChunks", serializedData); //TODO: Make this save properly. (No Serializer for the chunks!!!)
        ServerAPI.Logger.Debug("Saved chunk stability overrides");
    }

    private void OnSaveGameLoading() {
        if(ServerAPI == null) {
            return;
        }
        byte[] chunkdata = ServerAPI.WorldManager.SaveGame.GetData("setStabilityChunks");
        setChunks = chunkdata == null ? new() : SerializerUtil.Deserialize<Dictionary<string, float>>(chunkdata);
        ServerAPI.Logger.Debug("Loaded chunk stability overrides");
    }

    public override void Dispose() {
        harmony?.UnpatchAll(Mod.Info.ModID);
        base.Dispose();
    }
}
