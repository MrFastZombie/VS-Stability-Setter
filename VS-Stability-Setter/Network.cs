using Vintagestory.API.Server;
using ProtoBuf;
using System.Collections.Generic;

namespace VS_Stability_Setter;

class Network {
    ICoreServerAPI api;
    private readonly IServerNetworkChannel serverChannel;

    public Network(ICoreServerAPI api) {
        this.api = api;
        serverChannel = 
             api.Network.RegisterChannel("stabilitysetter")
                .RegisterMessageType(typeof(DataSyncPacket))
                .RegisterMessageType(typeof(UpdatePacket));
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class DataSyncPacket {
        public required string Response { get; set; }
        public required Dictionary<string, float> SendChunks { get; set; }
        public required int SendStabilityMode { get; set; }
        public required float SendGlobalStability { get; set; }
        public required float SendGlobalStabilityOffset { get; set; }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class UpdatePacket {
        public required string Response { get; set; }
        public required string SendChunkPosString { get; set; }
        public required float SendStability { get; set; }
        public required bool SendRemove { get; set; }
    }

    /// <summary>
    /// Broadcasts all relevent data to a player or the whole server.
    /// </summary>
    /// <param name="setChunks">The chunk cache to send.</param>
    /// <param name="StabilityMode">The current stability mode.</param>
    /// <param name="GlobalStability">Global stability value.</param>
    /// <param name="GlobalStabilityOffset">Global stability offset value.</param>
    /// <param name="player">If specified, only send to this player.</param>
    public void BroadcastData(Dictionary<string, float>  setChunks, int StabilityMode, float GlobalStability, float GlobalStabilityOffset, IServerPlayer? player) {
        dynamic? target;
        if(player == null) {
            target = api.World.AllOnlinePlayers as IServerPlayer[];
        } else target = player;
        
        if(api.World.AllOnlinePlayers.Length <= 0 && player == null) {
            api.Logger.Debug("[Stability Setter] Skipped BroadcastData sync, no online players detected.");
            return;
        }

        if(target == null) {
            api.Logger.Error("[Stability Setter] Skipped BroadcastData sync, target was null.");
            return;
        }

        serverChannel.SendPacket(new DataSyncPacket { Response = "data-sync",  SendChunks = setChunks, SendStabilityMode = StabilityMode, SendGlobalStability = GlobalStability, SendGlobalStabilityOffset = GlobalStabilityOffset}, target);
    }

    /// <summary>
    /// Broadcasts an updated chunk to all players.
    /// </summary>
    /// <param name="chunkPos">The chunkpos of the chunk to update.</param>
    /// <param name="stability">The new stability value.</param>
    /// <param name="remove">If true, remove this chunk from the cache for everyone.</param>
    public void BroadcastChunkUpdate(string chunkPos, float stability, bool remove) {
        serverChannel.SendPacket(new UpdatePacket { Response = "update", SendChunkPosString = chunkPos, SendStability = stability, SendRemove = remove }, api.World.AllPlayers as IServerPlayer[]);
    }
}