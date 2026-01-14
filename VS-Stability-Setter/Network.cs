using Vintagestory.API.Server;
using ProtoBuf;
using Vintagestory.API.MathTools;
using System.Collections.Generic;

namespace VS_Stability_Setter;

class Network {
    ICoreServerAPI api;
    private readonly IServerNetworkChannel serverChannel;

    public Network(ICoreServerAPI api) {
        this.api = api;
        serverChannel = 
             api.Network.RegisterChannel("stabilitysetter")
                .RegisterMessageType(typeof(NetworkApiMessage))
                .RegisterMessageType(typeof(NetworkApiResponse))
                .SetMessageHandler<NetworkApiMessage>(OnClientMessage);
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class NetworkApiMessage
    {
        public required string message;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class NetworkApiResponse
    {
        public required string response;
        public Dictionary<string, float>? sendChunks;
        public int? sendStabilityMode;
        public float? sendGlobalStability;
        public float? sendGlobalStabilityOffset;
        public string? sendChunkPosString;
        public float? sendStability;
        public bool? sendRemove;
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
            target = api.World.AllPlayers as IServerPlayer[];
        } else target = player;

        serverChannel.SendPacket(new NetworkApiResponse { response = "data-sync",  sendChunks = setChunks, sendStabilityMode = StabilityMode, sendGlobalStability = GlobalStability, sendGlobalStabilityOffset = GlobalStabilityOffset}, target);
    }

    /// <summary>
    /// Broadcasts an updated chunk to all players.
    /// </summary>
    /// <param name="chunkPos">The chunkpos of the chunk to update.</param>
    /// <param name="stability">The new stability value.</param>
    /// <param name="remove">If true, remove this chunk from the cache for everyone.</param>
    public void BroadcastChunkUpdate(string chunkPos, float stability, bool remove) {
        serverChannel.SendPacket(new NetworkApiResponse { response = "update", sendChunkPosString = chunkPos, sendStability = stability, sendRemove = remove }, api.World.AllPlayers as IServerPlayer[]);
    }

     //TODO: Probably remove this.
     public void OnClientMessage(IServerPlayer player, NetworkApiMessage msg) {
        if(msg.message.StartsWith("value?")) {
            BlockPos playerPos = player.WorldData.EntityPlayer.Pos.AsBlockPos;
            Vintagestory.GameContent.SystemTemporalStability StabSystem = api.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>();
            VS_Stability_Setter.VS_Stability_SetterModSystem.ServerChunkPos chunkPos = new(playerPos);
            float stability = StabSystem.GetTemporalStability(playerPos);
            serverChannel.SendPacket(new NetworkApiResponse { response = "value:" + stability.ToString() }, player);
        }
    }
}