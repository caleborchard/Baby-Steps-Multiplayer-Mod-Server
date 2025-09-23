using LiteNetLib;
using LiteNetLib.Utils;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BabyStepsServer
{
    class ClientInfo
    {
        public required NetPeer _peer;
        public required byte _uuid;
        public string? _displayName;
        public Color? _color;
        public bool collisionsEnabled = true;
        public bool jiminyState = false;
        public byte _lbKickoffPoint = 0;
        public byte[]? _latestRawBonePacket;
        public Vector3 position;
        public List<NetPeer>? distantClients;
        public Dictionary<byte, byte[]?> _savedPackets = new();
    }
    class Program : INetEventListener
    {
        // --- Constants ---
        private const byte OPCODE_UID = 0x01; // User ID
        private const byte OPCODE_ICC = 0x02; // Information Client Connection
        private const byte OPCODE_DCC = 0x03; // Dis connect Client
        private const byte OPCODE_UCI = 0x04; // Update Color Information (Includes nickname)
        private const byte OPCODE_UBP = 0x05; // Update Bone Position
        private const byte OPCODE_GWE = 0x06; // Generic World Event (Particles and sound effects)
        private const byte OPCODE_AAE = 0x07; // Accessory Add Event
        private const byte OPCODE_ARE = 0x08; // Accessory Remove Event
        private const byte OPCODE_JRE = 0x09; // Jiminy Ribbon Event
        private const byte OPCODE_CTE = 0xA; // Collision Toggle Event

        // --- Fields ---
        private NetManager _server;
        private Dictionary<NetPeer, ClientInfo> _clients = new();
        private readonly NetDataWriter writer = new();

        private byte _nextUUID = 0;
        private readonly Queue<byte> _availableUUIDs = new();
        private readonly HashSet<byte> _usedUUIDs = new();
        public Dictionary<byte, ushort> _lastSeenSequencePerClient = new();

        private float throttleUnits = 3f;
        private readonly int targetFPS = 60;
        private readonly Queue<(NetPeer from, byte[] data)> boneBroadcastQueue = new();
        private float boneVectorUpdateInterval = 5000f;

        private volatile bool _isCulling = false;
        private readonly object _clientLock = new object();

        private string configPath = "settings.cfg";
        private string password = "cuzzillobochfoddy";
        private int port = 7777;

        // --- Entry Point ---
        static void Main(string[] args)
        {
            new Program().Run();
        }
        public Program()
        {
            if (File.Exists(configPath))
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("port="))
                    {
                        if (int.TryParse(line.Substring("port=".Length), out int parsedPort))
                        {
                            if (parsedPort > 0 && parsedPort < 65535)
                            {
                                port = parsedPort;
                            }
                            else
                            {
                                Console.WriteLine("Invalid port in config!");
                                Environment.Exit(0);
                            }
                        }
                    }
                    else if (line.StartsWith("password="))
                    {
                        string parsedPwd = line.Substring("password=".Length);
                        if (parsedPwd.Length > 0)
                        {
                            password = parsedPwd;
                        }
                    }
                }
            }
            else
            {
                string[] lines =
                {
                    "port=7777",
                    "password="
                };
                File.WriteAllLines(configPath, lines);
                Console.WriteLine("No settings.cfg file found, creating default one");
            }

            _server = new NetManager(this)
            {
                AutoRecycle = true,
                IPv6Enabled = false,
                DisconnectTimeout = 15000
            };
        }
        public void Run()
        {
            _server.Start(port);
            Console.WriteLine($"Server started on UDP port {port} " + 
                (password == "cuzzillobochfoddy" ? "with no password" : $"with password {password}")
                );

            Stopwatch sw = new Stopwatch();
            sw.Start();
            int timeSinceLastBoneVectorUpdate = 0;

            while (true)
            {
                _server.PollEvents();
                HandleBoneBroadcasts();

                sw.Stop();
                timeSinceLastBoneVectorUpdate += (int)sw.ElapsedMilliseconds;

                if (timeSinceLastBoneVectorUpdate >= boneVectorUpdateInterval && !_isCulling)
                {
                    timeSinceLastBoneVectorUpdate = 0;
                    _isCulling = true;

                    Task.Run(() => CullDistantClients());
                }

                sw.Restart();
                Thread.Sleep(1000 / targetFPS);
            }
        }

        // --- Network ---
        private void HandleBoneBroadcasts()
        {
            int allowedSends = (int)(throttleUnits * targetFPS);

            for (int i = 0; i < allowedSends && boneBroadcastQueue.Count > 0; i++)
            {
                var (from, data) = boneBroadcastQueue.Dequeue();
                if (!_clients.ContainsKey(from)) continue;
                writer.Reset();
                ushort totalLength = (ushort)(data.Length + 2);
                writer.Put(totalLength);
                writer.Put(data);

                foreach (var client in _clients)
                {
                    if (client.Key == from || _clients[from].distantClients?.Contains(client.Key) == true) continue;
                    client.Key.Send(writer, DeliveryMethod.Unreliable);
                }
            }
        }
        public void OnPeerConnected(NetPeer peer)
        {
            byte uuid = AllocateUUID();

            if (uuid == 255)
            {
                Console.WriteLine("Client rejected: server full");
                peer.Disconnect();
                return;
            }

            Console.WriteLine($"Client connected: {uuid}");

            var info = new ClientInfo { _peer = peer, _uuid = uuid };
            _clients[peer] = info;

            Send(peer, new byte[] { OPCODE_UID, uuid }, DeliveryMethod.ReliableOrdered);

            foreach (var client in _clients)
            {
                if (client.Key == peer) continue;

                Send(peer, new byte[] { OPCODE_ICC, client.Value._uuid }, DeliveryMethod.ReliableOrdered);

                var infoPacket = GetClientInfoPacket(client.Key);
                if (infoPacket != null) Send(peer, infoPacket, DeliveryMethod.ReliableOrdered);

                foreach (var savedPacket in client.Value._savedPackets)
                {
                    if (savedPacket.Value != null)
                    {
                        Send(peer, savedPacket.Value, DeliveryMethod.ReliableOrdered);
                    }
                }
            }
        }
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (_clients.TryGetValue(peer, out var client))
            {
                byte uuid = client._uuid;
                Console.WriteLine($"Client {uuid} disconnected: {disconnectInfo.Reason}");

                Broadcast(new byte[] { OPCODE_DCC, uuid }, DeliveryMethod.ReliableOrdered, exclude: peer);

                _clients.Remove(peer);
                _lastSeenSequencePerClient.Remove(uuid);
                ReclaimUUID(uuid);
            }
        }
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNum, DeliveryMethod deliveryMethod)
        {
            byte[] fullData = reader.GetRemainingBytes();
            reader.Recycle();

            if (fullData.Length < 2)
            {
                Console.WriteLine("Received packet too short to contain length header.");
                return;
            }

            ushort declaredLength = BitConverter.ToUInt16(fullData, 0);
            if (declaredLength != fullData.Length)
            {
                Console.WriteLine($"Packet length mismatch: expected {declaredLength}, got {fullData.Length}");
                return;
            }

            byte[] data = new byte[fullData.Length - 2];
            Buffer.BlockCopy(fullData, 2, data, 0, data.Length);

            if (!_clients.TryGetValue(peer, out var client)) return;

            byte opcode = data[0];
            HandleOpcode(peer, client, opcode, data);
        }
        public void OnConnectionRequest(ConnectionRequest request)
        {
            string incomingKey = request.Data.GetString();

            if (incomingKey == password) request.Accept();
            else
            {
                request.Reject();
                Console.WriteLine($"Client tried to connect with incorrect password");
            }
        }
        public void OnNetworkReceiveUnconnected(IPEndPoint endPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Console.WriteLine($"Socket error: {socketError}");
        }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        // --- Helpers ---
        private void CullDistantClients()
        {
            lock (_clientLock)
            {
                foreach (var kvp in _clients)
                {
                    var client = kvp.Value;
                    var data = client._latestRawBonePacket;

                    if (data != null && data.Length >= 29)
                    {
                        float posX = BitConverter.ToSingle(data, 1);
                        float posY = BitConverter.ToSingle(data, 5);
                        float posZ = BitConverter.ToSingle(data, 9);
                        client.position = new Vector3(posX, posY, posZ);
                    }
                }

                foreach (var kvpA in _clients)
                {
                    var clientA = kvpA.Value;
                    var posA = clientA.position;
                    clientA.distantClients = new List<NetPeer>();

                    foreach (var kvpB in _clients)
                    {
                        if (kvpA.Key == kvpB.Key) continue;
                        var clientB = kvpB.Value;
                        float distance = Vector3.Distance(posA, clientB.position);
                        if (distance > 100f)
                            clientA.distantClients.Add(kvpB.Key);
                    }
                }
            }
            _isCulling = false;
        }
        private void HandleOpcode(NetPeer peer, ClientInfo client, byte opcode, byte[] data)
        {
            switch (opcode)
            {
                case 1: HandleBoneInfo(peer, client, data); break;
                case 2: HandleClientInfo(peer, client, data); break;
                case 3: HandleWorldEvent(peer, client, data); break;
                case 4: HandleAccessoryAdd(peer, client, data); break;
                case 5: HandleAccessoryRemove(peer, client, data); break;
                case 6: HandleJiminyUpdate(peer, client, data); break;
                case 7: HandleCollisionToggleUpdate(peer, client, data); break;
                default: Console.WriteLine($"{client._uuid}: Unknown opcode {opcode}"); break;
            }
        }
        private void HandleBoneInfo(NetPeer peer, ClientInfo client, byte[] data)
        {
            if (client._color == null || client._displayName == null) return;
            byte senderKickoff = data[1];
            ushort seq = BitConverter.ToUInt16(data, 2);
            byte[] rawTransformData = data[4..];

            if (!_lastSeenSequencePerClient.TryGetValue(client._uuid, out ushort lastSeq) || IsNewer(seq, lastSeq))
            {
                _lastSeenSequencePerClient[client._uuid] = seq;
                client._lbKickoffPoint = senderKickoff;
                client._latestRawBonePacket = rawTransformData;

                List<byte> packet = new() { OPCODE_UBP, client._uuid, client._lbKickoffPoint };
                packet.AddRange(BitConverter.GetBytes(seq));
                packet.AddRange(rawTransformData);

                boneBroadcastQueue.Enqueue((peer, packet.ToArray()));
            }
        }
        private void HandleClientInfo(NetPeer peer, ClientInfo client, byte[] data)
        {
            var firstReceive = false;
            if (client._color == null || client._displayName == null) firstReceive = true;

            client._color = Color.FromArgb(data[1], data[2], data[3]);
            client._displayName = Encoding.UTF8.GetString(data, 4, data.Length - 4);

            Console.WriteLine($"{client._uuid}: Set color to {client._color}");
            Console.WriteLine($"{client._uuid}: Set nickname to {client._displayName}");

            if (firstReceive) Broadcast(new byte[] { OPCODE_ICC, client._uuid }, DeliveryMethod.ReliableOrdered, exclude: peer);
            Broadcast(GetClientInfoPacket(peer), DeliveryMethod.ReliableOrdered, exclude: peer);
        }
        private void HandleWorldEvent(NetPeer peer, ClientInfo client, byte[] data)
        {
            ushort seq = BitConverter.ToUInt16(data, 1);
            byte[] rawGWEData = data[3..];

            if (!_lastSeenSequencePerClient.TryGetValue(client._uuid, out ushort lastSeq) || IsNewer(seq, lastSeq))
            {
                _lastSeenSequencePerClient[client._uuid] = seq;
                List<byte> packet = new() { OPCODE_GWE, client._uuid };
                packet.AddRange(BitConverter.GetBytes(seq));
                packet.AddRange(rawGWEData);

                boneBroadcastQueue.Enqueue((peer, packet.ToArray()));
            }
        }
        private void HandleAccessoryAdd(NetPeer peer, ClientInfo client, byte[] data)
        {
            List<byte> packet = new() { OPCODE_AAE, client._uuid };
            packet.AddRange(data[1..]);
            var packetArray = packet.ToArray();

            client._savedPackets[data[1]] = packetArray;

            Broadcast(packetArray, DeliveryMethod.ReliableOrdered, exclude: peer);
        }
        private void HandleAccessoryRemove(NetPeer peer, ClientInfo client, byte[] data)
        {
            List<byte> packet = new() { OPCODE_ARE, client._uuid };
            packet.AddRange(data[1..]);

            client._savedPackets[data[1]] = null;

            Broadcast(packet.ToArray(), DeliveryMethod.ReliableOrdered, exclude: peer);
        }
        private void HandleJiminyUpdate(NetPeer peer, ClientInfo client, byte[] data)
        {
            client.jiminyState = Convert.ToBoolean(data[1]);

            byte[] packet = { OPCODE_JRE, client._uuid, Convert.ToByte(client.jiminyState) };

            client._savedPackets[0x02] = packet;

            Broadcast(packet, DeliveryMethod.ReliableOrdered, exclude: peer);
        }

        private void HandleCollisionToggleUpdate(NetPeer peer, ClientInfo client, byte[] data)
        {
            client.collisionsEnabled = Convert.ToBoolean(data[1]);

            byte[] packet = { OPCODE_CTE, client._uuid, Convert.ToByte(client.collisionsEnabled) };

            client._savedPackets[0x03] = packet;

            Broadcast(packet, DeliveryMethod.ReliableOrdered, exclude: peer);
        }

        // --- UUID Management ---
        private byte AllocateUUID()
        {
            if (_availableUUIDs.Count > 0)
            {
                byte uuid = _availableUUIDs.Dequeue();
                _usedUUIDs.Add(uuid);
                return uuid;
            }

            if (_nextUUID < 255)
            {
                byte uuid = _nextUUID++;
                _usedUUIDs.Add(uuid);
                return uuid;
            }

            Console.WriteLine("Maximum number of clients reached!");
            return 255;
        }
        private void ReclaimUUID(byte uuid)
        {
            if (_usedUUIDs.Remove(uuid)) _availableUUIDs.Enqueue(uuid);
        }

        // --- Utilities ---
        static bool IsNewer(ushort current, ushort previous)
        {
            return (ushort)(current - previous) < 32768;
        }
        private byte[] GetClientInfoPacket(NetPeer peer)
        {
            ClientInfo info = _clients[peer];
            if (info._color == null || string.IsNullOrEmpty(info._displayName))
            {
                Console.WriteLine($"Warning: Missing client info for UUID {info._uuid}");
                return new byte[0];
            }

            List<byte> final = new()
            {
                OPCODE_UCI,
                info._uuid,
                info._color.Value.R,
                info._color.Value.G,
                info._color.Value.B
            };

            final.AddRange(Encoding.UTF8.GetBytes(info._displayName));
            return final.ToArray();
        }
        private void Send(NetPeer peer, byte[] packet, DeliveryMethod deliveryMethod)
        {
            writer.Reset();
            ushort totalLength = (ushort)(packet.Length + 2);
            writer.Put(totalLength);
            writer.Put(packet);
            peer.Send(writer, deliveryMethod);
        }
        private void Broadcast(byte[] packet, DeliveryMethod deliveryMethod, NetPeer? exclude)
        {
            writer.Reset();
            ushort totalLength = (ushort)(packet.Length + 2);
            writer.Put(totalLength);
            writer.Put(packet);

            foreach (var client in _clients)
            {
                if (client.Key == exclude) continue;
                client.Key.Send(writer, deliveryMethod);
            }
        }
    }
}