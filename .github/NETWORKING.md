# Contributing to Netcode
## Threads
The client runs on 2 threads; the Godot thread and the ENet thread. Never run Godot code in the ENet thread and likewise never run ENet code in the Godot thread. If you ever need to communicate between the threads, use the proper `ConcurrentQueue`'s in `ENetClient.cs`.

## Networking
The netcode utilizes [ENet-CSharp](https://github.com/SoftwareGuy/ENet-CSharp/blob/master/DOCUMENTATION.md), a reliable UDP networking library.

Never give the client any authority, the server always has the final say in everything. This should always be thought of when sending new packets.

Packets are sent like this.
```cs
// WPacketChatMessage.cs
namespace KRU.Networking
{
    public class WPacketPlayerData : IWritable
    {
        public uint PlayerId { get; set; }
        public uint PlayerHealth { get; set; }
        public string PlayerName { get; set; }

        public void Write(PacketWriter writer)
        {
            writer.Write(PlayerId);
            writer.Write(PlayerHealth);
            writer.Write(PlayerName);
        }
    }
}

// Since packets are being enqueued to a ConcurrentQueue they can be called from any thread
ENetClient.Outgoing.Enqueue(new ClientPacket((byte)ClientPacketOpcode.PlayerData, new WPacketPlayerData {
    PlayerId = 0,
    PlayerHealth = 100,
    PlayerName = "Steve"
}));
```
