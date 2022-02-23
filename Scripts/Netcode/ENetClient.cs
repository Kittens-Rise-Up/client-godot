using Godot;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using ENet;
using Common.Netcode;
using Client.UI;
using Client.Utilities;

using Thread = System.Threading.Thread; // CS0104: Ambigious reference between 'Godot.Thread' and 'System.Threading.Thread'
using Version = Common.Netcode.Version; // CS0104: Ambiguous reference between 'Common.Netcode.Version' and 'System.Version'

namespace Client.Netcode
{
    public abstract class ENetClient : Node
    {
        public static readonly ConcurrentQueue<ClientPacket> Outgoing = new ConcurrentQueue<ClientPacket>();
        public static readonly ConcurrentQueue<ENetCmd> ENetCmds = new ConcurrentQueue<ENetCmd>();

        private static readonly ConcurrentBag<Packet> Incoming = new ConcurrentBag<Packet>();
        private static readonly ConcurrentQueue<GodotCmd> GodotCmds = new ConcurrentQueue<GodotCmd>();
        private static readonly Dictionary<ServerPacketOpcode, HandlePacket> HandlePacket = typeof(HandlePacket).Assembly.GetTypes().Where(x => typeof(HandlePacket).IsAssignableFrom(x) && !x.IsAbstract).Select(Activator.CreateInstance).Cast<HandlePacket>().ToDictionary(x => x.Opcode, x => x);
        public static bool ENetThreadRunning;
        private static bool RunningNetCode;

        public virtual void ProcessGodotCommands(GodotCmd cmd) {}
        public virtual void Connect(Event netEvent, string jwt) {}
        public virtual void Disconnect(Event netEvent) {}
        public virtual void Timeout(Event netEvent) {}

        public override void _Process(float delta)
        {
            while (GodotCmds.TryDequeue(out GodotCmd cmd)) 
            {
                if (cmd.Opcode == GodotOpcode.ENetPacket)
                {
                    var packetReader = (PacketReader)cmd.Data[0];
                    var opcode = (ServerPacketOpcode)packetReader.ReadByte();

                    HandlePacket[opcode].Handle(packetReader);
                    packetReader.Dispose();
                    return;
                }
                
                ProcessGodotCommands(cmd);
            }
        }

        public void Connect(string ip, ushort port, string jwt) 
        {
            if (ENetThreadRunning) 
            {
                GD.Print("ENet thread is running already");
                return;
            }
            
            ENetThreadRunning = true;
            Task.Run(() => ENetThreadWorker(ip, port, jwt));
        }

        private void ENetThreadWorker(string ip, ushort port, string jwt)
        {
            Library.Initialize();
            GDLog("ENet library initialized successfully");
            var wantsToExit = false;
            var wantsToDisconnect = false;

            using (var client = new Host()) 
            {
                var address = new Address();

                address.SetHost(ip);
                address.Port = port;
                client.Create();

                //GDLog("Attempting to connect to the game server...");
                var peer = client.Connect(address);

                uint pingInterval = 1000; // Pings are used both to monitor the liveness of the connection and also to dynamically adjust the throttle during periods of low traffic so that the throttle has reasonable responsiveness during traffic spikes.
                uint timeout = 5000; // Will be ignored if maximum timeout is exceeded
                uint timeoutMinimum = 5000; // The timeout for server not sending the packet to the client sent from the server
                uint timeoutMaximum = 5000; // The timeout for server not receiving the packet sent from the client

                peer.PingInterval(pingInterval); 
                peer.Timeout(timeout, timeoutMinimum, timeoutMaximum);

                RunningNetCode = true;
                while (RunningNetCode) {
                    var polled = false;

                    // ENet Cmds from Godot Thread
                    while (ENetCmds.TryDequeue(out ENetCmd cmd))
                    {
                        switch (cmd.Opcode)
                        {
                            case ENetOpcode.ClientWantsToExitApp:
                                peer.Disconnect(0);
                                RunningNetCode = false;
                                wantsToExit = true;
                                break;
                            case ENetOpcode.ClientWantsToDisconnect:
                                peer.Disconnect(0);
                                RunningNetCode = false;
                                wantsToDisconnect = true;
                                break;
                        }
                    }

                    // Incoming
                    while (Incoming.TryTake(out Packet packet))
                        GodotCmds.Enqueue(new GodotCmd { Opcode = GodotOpcode.ENetPacket, Data = new List<object> { new PacketReader(packet) }});

                    // Outgoing
                    while (Outgoing.TryDequeue(out ClientPacket clientPacket))
                    {
                        byte channelID = 0; // The channel all networking traffic will be going through
                        var packet = default(Packet);
                        packet.Create(clientPacket.Data, clientPacket.PacketFlags);
                        peer.Send(channelID, ref packet);
                    }

                    while (!polled) {
                        if (client.CheckEvents(out Event netEvent) <= 0) {
                            if (client.Service(15, out netEvent) <= 0)
                                break;

                            polled = true;
                        }

                        switch (netEvent.Type)
                        {
                            case EventType.Connect:
                                Connect(netEvent, jwt);
                                break;
                            case EventType.Receive:
                                // Receive
                                var packet = netEvent.Packet;
                                if (packet.Length > GamePacket.MaxSize) 
                                {
                                    GDLog($"Tried to read packet from server of size {packet.Length} when max packet size is {GamePacket.MaxSize}");
                                    packet.Dispose();
                                    continue;
                                }

                                Incoming.Add(netEvent.Packet);
                                break;
                            case EventType.Timeout:
                                Timeout(netEvent);
                                break;
                            case EventType.Disconnect:
                                Disconnect(netEvent);
                                break;
                        }
                    }
                }

                client.Flush();
            }

            Library.Deinitialize();
            ENetThreadRunning = false;

            if (wantsToDisconnect)
                GodotCmds.Enqueue(new GodotCmd { Opcode = GodotOpcode.LoadMainMenu });

            if (wantsToExit)
                GodotCmds.Enqueue(new GodotCmd { Opcode = GodotOpcode.ExitApp });
        }

        protected static void GDLog(string text) => GodotCmds.Enqueue(new GodotCmd { Opcode = GodotOpcode.LogMessage, Data = new List<object> { text }});
    }

    public class GodotCmd 
    {
        public GodotOpcode Opcode { get; set; }
        public List<object> Data { get; set; }
    }

    public class ENetCmd 
    {
        public ENetOpcode Opcode { get; set; }
        public List<object> Data { get; set; }
    }

    public enum GodotOpcode 
    {
        ENetPacket,
        LogMessage,
        LoadMainMenu,
        ExitApp
    }

    public enum ENetOpcode 
    {
        ClientWantsToExitApp,
        ClientWantsToDisconnect
    }
}
