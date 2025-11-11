using BabyStepsMultiplayerServer;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BabyStepsServer
{
    class Program : INetEventListener
    {
        // --- Constants ---
        private const byte OPCODE_UID = 0x01;
        private const byte OPCODE_ICC = 0x02;
        private const byte OPCODE_DCC = 0x03;
        private const byte OPCODE_UCI = 0x04;
        private const byte OPCODE_UBP = 0x05;
        private const byte OPCODE_GWE = 0x06;
        private const byte OPCODE_AAE = 0x07;
        private const byte OPCODE_ARE = 0x08;
        private const byte OPCODE_JRE = 0x09;
        private const byte OPCODE_CTE = 0x0A;
        private const byte OPCODE_CMS = 0x0B;
        private const byte OPCODE_PCF = 0x0C;

        private const string SERVER_VERSION = "105";
        private const string GITHUB_REPO = "https://github.com/caleborchard/Baby-Steps-Multiplayer-Mod-Server";

        // --- Fields ---
        private NetManager _server;
        private Dictionary<NetPeer, ClientInfo> _clients = new();
        private readonly NetDataWriter writer = new();
        private ServerSettings _settings;
        private BandwidthManager _bandwidthManager;

        private HashSet<byte> _usedUUIDs = new HashSet<byte>();
        private const byte MAX_UUID = 254; // Reserve 255 for errors
        public Dictionary<byte, ushort> _lastSeenSequencePerClient = new();

        private readonly int targetFPS = 60;
        private volatile bool _isCulling = false;
        private readonly object _clientLock = new object();

        // --- Entry Point ---
        static async Task Main(string[] args)
        {
            try
            {
                Console.Clear();
                Console.SetOut(new TimestampedTextWriter(Console.Out));
                await new Program().Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex}");
                Console.ReadLine();
            }
        }

        public Program()
        {
            _settings = ServerSettings.Load();
            _bandwidthManager = new BandwidthManager(_settings, targetFPS, _clients);

            _server = new NetManager(this)
            {
                AutoRecycle = true,
                IPv6Enabled = false,
                DisconnectTimeout = 15000
            };
        }

        public async Task Run()
        {
            CheckForUpdates();

            _server.Start(_settings.Port);
            Console.WriteLine($"Server started on UDP port {_settings.Port} " +
                (_settings.Password == "cuzzillobochfoddy" ? "with no password" : $"with password {_settings.Password}")
            );
            Console.WriteLine($"Bandwidth limit: {_settings.MaxBandwidthKbps} KB/s | Telemetry: {(_settings.TelemetryEnabled ? "ON" : "OFF")}");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            int timeSinceLastBoneVectorUpdate = 0;

            while (true)
            {
                _server.PollEvents();
                _bandwidthManager.ProcessQueues();

                sw.Stop();
                timeSinceLastBoneVectorUpdate += (int)sw.ElapsedMilliseconds;

                if (timeSinceLastBoneVectorUpdate >= _settings.StaticUpdateRate && !_isCulling)
                {
                    timeSinceLastBoneVectorUpdate = 0;
                    _isCulling = true;

                    // Unlikely to cause errors which is why I'm fine not awaiting this to keep things running smoothly
                    Task.Run(() => CullDistantClients()); 
                }

                sw.Restart();
                //Thread.Sleep(1000 / targetFPS);
                await Task.Delay(1000 / targetFPS);
            }
        }

        private void CheckForUpdates()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "BabyStepsServer");
                    client.Timeout = TimeSpan.FromSeconds(5);

                    var response = client.GetAsync("https://api.github.com/repos/caleborchard/Baby-Steps-Multiplayer-Mod-Server/releases/latest").Result;

                    if (response.IsSuccessStatusCode)
                    {
                        var json = response.Content.ReadAsStringAsync().Result;
                        var doc = JsonDocument.Parse(json);
                        var latestTag = doc.RootElement.GetProperty("tag_name").GetString();

                        if (latestTag != null)
                        {
                            // Remove 'v' prefix if present
                            latestTag = latestTag.TrimStart('v');

                            // Convert version strings to comparable format (remove dots)
                            string currentVersion = SERVER_VERSION;
                            string latestVersion = latestTag.Replace(".", "");

                            // Parse versions as integers for proper comparison
                            if (int.TryParse(currentVersion, out int currentVer) && int.TryParse(latestVersion, out int latestVer))
                            {
                                if (currentVer < latestVer)
                                {
                                    Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
                                    Console.WriteLine("║                     VERSION WARNING                            ║");
                                    Console.WriteLine("╠════════════════════════════════════════════════════════════════╣");
                                    Console.WriteLine($"║  Your server version: {currentVersion.PadRight(40)} ║");
                                    Console.WriteLine($"║  Latest version:      {latestVersion.PadRight(40)} ║");
                                    Console.WriteLine("║                                                                ║");
                                    Console.WriteLine("║  Your server is OUTDATED!                                      ║");
                                    Console.WriteLine("║  Please update to the latest version.                          ║");
                                    Console.WriteLine("║                                                                ║");
                                    Console.WriteLine("║  Download: github.com/caleborchard/                            ║");
                                    Console.WriteLine("║            Baby-Steps-Multiplayer-Mod-Server                   ║");
                                    Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
                                    Console.WriteLine();
                                }
                                else if (currentVer > latestVer)
                                {
                                    Console.WriteLine($"Server version ({currentVersion}) is newer than latest release ({latestVersion}) - development build");
                                }
                                else
                                {
                                    Console.WriteLine($"Server is up to date (version {currentVersion})");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to check for updates: {ex.Message}");
            }
        }

        // --- Network Event Handlers ---
        public void OnPeerConnected(NetPeer peer)
        {
            byte uuid = AllocateUUID();

            if (uuid == 255)
            {
                Console.WriteLine("Client rejected: server full");
                peer.Disconnect();
                return;
            }

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
                Console.WriteLine($"Player {client._displayName}[{uuid}] disconnected: {disconnectInfo.Reason}");

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

            string assembledPassword = SERVER_VERSION + _settings.Password;
            if (incomingKey == assembledPassword) request.Accept();
            else
            {
                request.Reject();
                if (incomingKey.StartsWith(SERVER_VERSION))
                {
                    Console.WriteLine($"Client tried to connect with incorrect password: {incomingKey.Substring(SERVER_VERSION.Length)}");
                }
                else
                {
                    Console.WriteLine($"Outdated client has attempted a connection and been rejected.");
                }
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint endPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Console.WriteLine($"Socket error: {socketError}");
        }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        // --- Helper Methods ---
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
                        float posZ = BitConverter.ToSingle(data, 8);
                        client.position = new Vector3(0, 0, posZ);
                    }
                }

                foreach (var kvpA in _clients)
                {
                    var clientA = kvpA.Value;
                    clientA.distantClients = new List<NetPeer>();

                    foreach (var kvpB in _clients)
                    {
                        if (kvpA.Key == kvpB.Key) continue;
                        float distance = Math.Abs(clientA.position.Z - kvpB.Value.position.Z);
                        if (distance > _settings.DistanceCutoff)
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
                case 8: HandleChatMessage(peer, client, data); break;
                case 9: HandleAudioFrame(peer, client, data); break;
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

                _bandwidthManager.EnqueueBoneUpdate(peer, packet.ToArray());
            }
        }

        private void HandleClientInfo(NetPeer peer, ClientInfo client, byte[] data)
        {
            var firstReceive = false;
            if (client._color == null || client._displayName == null) firstReceive = true;

            client._color = new RGBColor(data[1], data[2], data[3]);

            string receivedName = Encoding.UTF8.GetString(data, 4, data.Length - 4);
            if (client._displayName == null) Console.WriteLine($"Player {receivedName}[{client._uuid}] has connected.");
            else Console.WriteLine($"{client._displayName}[{client._uuid}] changed nickname to {receivedName}");
            client._displayName = receivedName;

            Console.WriteLine($"{client._displayName}[{client._uuid}] set color to {client._color.ToString()}");

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

                _bandwidthManager.EnqueueHighPriority(peer, packet.ToArray());
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

        private void HandleChatMessage(NetPeer peer, ClientInfo client, byte[] data)
        {
            List<byte> packet = new();
            packet.Add(OPCODE_CMS);
            packet.Add(client._uuid);

            byte[] dataToSend = data.Skip(1).ToArray();
            packet.AddRange(dataToSend);

            Console.WriteLine($"{client._displayName}[{client._uuid}]: {Encoding.UTF8.GetString(dataToSend)}");

            Broadcast(packet.ToArray(), DeliveryMethod.ReliableOrdered, exclude: peer);
        }

        private void HandleAudioFrame(NetPeer peer, ClientInfo client, byte[] data)
        {
            if (!_settings.VoiceChatEnabled) return;

            List<byte> packet = new();
            packet.Add(OPCODE_PCF);
            packet.Add(client._uuid);

            packet.AddRange(data[1..]);

            _bandwidthManager.EnqueueAudioFrame(peer, packet.ToArray());
        }

        // --- UUID Management ---
        private byte AllocateUUID()
        {
            for (byte i = 0; i <= MAX_UUID; i++)
            {
                if (!_usedUUIDs.Contains(i))
                {
                    _usedUUIDs.Add(i);
                    return i;
                }
            }

            Console.WriteLine("Maximum number of clients reached!");
            return 255;
        }

        private void ReclaimUUID(byte uuid)
        {
            _usedUUIDs.Remove(uuid);
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
                info._color.R,
                info._color.G,
                info._color.B
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