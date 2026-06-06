using BabyStepsNetworking.Host;
using BabyStepsNetworking.Packets;
using BabyStepsNetworking.Shared;
using BabyStepsNetworking.Transport;
using BabyStepsNetworking.Transport.LiteNetLib;
using BabyStepsMultiplayerServer;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace BabyStepsServer;

class Program
{
    private const string SERVER_VERSION = "108";
    private const string GITHUB_REPO = "https://github.com/caleborchard/Baby-Steps-Multiplayer-Mod-Server";

    private NetworkHost _host;
    private DiscordWebhookHelper _discord;
    private ServerLifecycleTracker _lifecycle;
    private ServerSettings _serverSettings;
    private volatile bool _isRunning = true;
    private bool _shutdownRecorded = false;
    private CancellationTokenSource _statusCts;
    private readonly Dictionary<byte, bool> _easterEggActive = new();

    static async Task Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Clear();
            Console.SetOut(new TimestampedTextWriter(Console.Out));
            await new Program(args).Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex}");
            Console.ReadLine();
        }
    }

    public Program(string[]? args = null)
    {
        _serverSettings = ServerSettings.Load(args);

        var hostSettings = _serverSettings.ToHostSettings(SERVER_VERSION);
        var transport = new LiteNetLibServerTransport();
        _host = new NetworkHost(transport, hostSettings);
        _host.Log += Console.WriteLine;
        _host.ClientConnected += OnClientConnected;
        _host.ClientDisconnected += OnClientDisconnected;

        RegisterHandlers();

        _discord = new DiscordWebhookHelper(_serverSettings.DiscordWebhookUrl, _serverSettings.DiscordWebhookEnabled);
        _lifecycle = new ServerLifecycleTracker();

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _isRunning = false; };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RecordCleanShutdown();
    }

    private void RegisterHandlers()
    {
        _host.RegisterHandler(CoreClientToServerOpcode.BoneUpdate,       HandleBoneUpdate);
        _host.RegisterHandler(CoreClientToServerOpcode.PlayerInfo,       HandlePlayerInfo);
        _host.RegisterHandler(CoreClientToServerOpcode.WorldEvent,       HandleWorldEvent);
        _host.RegisterHandler(CoreClientToServerOpcode.AccessoryAdd,     HandleAccessoryAdd);
        _host.RegisterHandler(CoreClientToServerOpcode.AccessoryRemove,  HandleAccessoryRemove);
        _host.RegisterHandler(CoreClientToServerOpcode.JiminyRibbon,     HandleJiminyUpdate);
        _host.RegisterHandler(CoreClientToServerOpcode.CollisionToggle,  HandleCollisionToggle);
        _host.RegisterHandler(CoreClientToServerOpcode.ChatMessage,      HandleChatMessage);
        _host.RegisterHandler(CoreClientToServerOpcode.AudioFrame,       HandleAudioFrame);
    }

    public async Task Run()
    {
        CheckForUpdates();

        var startup = _lifecycle.MarkServerStarted();
        _ = _discord.SendServerStartedAsync(startup.Downtime, startup.PreviousRunLikelyCrashed);

        _host.Start();
        StartStatusListener();

        var pw = _serverSettings.Password;
        Console.WriteLine($"Bandwidth: {_serverSettings.MaxBandwidthKbps} KB/s | Telemetry: {(_serverSettings.TelemetryEnabled ? "ON" : "OFF")}");
        Console.WriteLine($"Discord Webhook: {(_serverSettings.DiscordWebhookEnabled ? "ON" : "OFF")}");

        var frameSw = Stopwatch.StartNew();
        long lastHeartbeatMs = 0;
        const int targetFps = 60;

        try
        {
            while (_isRunning)
            {
                float deltaMs = (float)frameSw.Elapsed.TotalMilliseconds;
                frameSw.Restart();

                _host.Tick(deltaMs);

                if (_host.UptimeMs - lastHeartbeatMs >= 5000)
                {
                    _lifecycle.UpdateHeartbeat();
                    lastHeartbeatMs = _host.UptimeMs;
                }

                await Task.Delay(1000 / targetFps);
            }
        }
        finally
        {
            RecordCleanShutdown();
            _host.Stop();
        }
    }

    private void RecordCleanShutdown()
    {
        if (_shutdownRecorded) return;
        _shutdownRecorded = true;
        _statusCts?.Cancel();
        _lifecycle.MarkCleanShutdown();
    }

    private void StartStatusListener()
    {
        int statusPort = _serverSettings.Port + 1;
        _statusCts = new CancellationTokenSource();
        var token   = _statusCts.Token;

        bool isLocked = !string.IsNullOrEmpty(_serverSettings.Password)
                     && _serverSettings.Password != "cuzzillobochfoddy";

        Task.Run(() =>
        {
            try
            {
                using var udp = new UdpClient(statusPort);
                udp.Client.ReceiveTimeout = 500;
                Console.WriteLine($"Status listener on port {statusPort}");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var ep   = new IPEndPoint(IPAddress.Any, 0);
                        var data = udp.Receive(ref ep);

                        if (data.Length >= 4
                            && data[0] == 'B' && data[1] == 'B'
                            && data[2] == 'S' && data[3] == 'Q')
                        {
                            // Only count players who have fully introduced themselves
                            int count = _host?.Clients.Values
                                .Count(c => c.InfoPacket != null) ?? 0;

                            var resp = new byte[]
                            {
                                (byte)'B', (byte)'B', (byte)'S', (byte)'R',
                                (byte)Math.Clamp(count, 0, 255),
                                16,
                                (byte)(isLocked ? 1 : 0)
                            };
                            udp.Send(resp, resp.Length, ep);
                        }
                    }
                    catch (SocketException) { /* receive timeout — loop again */ }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Status listener error: {ex.Message}");
            }
        }, token);
    }

    // --- Client lifecycle ---

    private void OnClientConnected(ConnectedClient client)
    {
        // Nothing extra on connect; display name arrives with PlayerInfo packet
    }

    private void OnClientDisconnected(ConnectedClient client)
    {
        _easterEggActive.Remove(client.Uuid);
        _ = _discord.SendPlayerLeftAsync(client.DisplayName ?? "Unknown", client.Uuid, _host.Clients.Count);
    }

    // --- Packet handlers ---

    private void HandleBoneUpdate(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        // data (opcode already stripped): [kickoff][seq_lo][seq_hi][raw_bone_data...]
        if (!sender.IsInitialized) return;
        if (data.Length < 4) return;

        byte kickoff = data[0];
        ushort seq = BitConverter.ToUInt16(data, 1);
        byte[] rawBone = data[3..];

        if (!host.IsNewerSequence(sender.Uuid, seq)) return;

        sender.LastBoneKickoffPoint = kickoff;
        sender.LatestRawBoneData = rawBone;

        var packet = PacketBuilder.Build(CoreServerToClientOpcode.BoneUpdate, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.Add(kickoff);
            bytes.AddRange(BitConverter.GetBytes(seq));
            bytes.AddRange(rawBone);
            bytes.AddRange(BitConverter.GetBytes(host.UptimeMs));
        });

        host.EnqueueBoneUpdate(sender.PeerId, packet);
    }

    private void HandlePlayerInfo(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        // data (opcode already stripped): [R][G][B][egg_flag][name_utf8...]   name may be empty
        if (data.Length < 4) return;

        bool firstReceive = !sender.IsInitialized;
        sender.Color = new RGBColor(data[0], data[1], data[2]);
        _easterEggActive[sender.Uuid] = data[3] != 0;
        int nameLen = data.Length - 4;
        string name = nameLen > 0 ? Encoding.UTF8.GetString(data, 4, nameLen) : string.Empty;

        if (sender.DisplayName == null)
        {
            Console.WriteLine($"Player {name}[{sender.Uuid}] has connected.");
            sender.DisplayName = name;
            _ = _discord.SendPlayerJoinedAsync(name, sender.Uuid, host.Clients.Count);
        }
        else if (sender.DisplayName != name)
        {
            Console.WriteLine($"{sender.DisplayName}[{sender.Uuid}] -> {name}");
            _ = _discord.SendPlayerNameChangedAsync(sender.DisplayName, name, sender.Uuid);
            sender.DisplayName = name;
        }

        Console.WriteLine($"{sender.DisplayName}[{sender.Uuid}] color={sender.Color.Value.GetString()}");

        // Build and cache the S2C info packet so new joiners receive it
        var infoPacket = PacketBuilder.Build(CoreServerToClientOpcode.PlayerInfoUpdate, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.Add(sender.Color.Value.R);
            bytes.Add(sender.Color.Value.G);
            bytes.Add(sender.Color.Value.B);
            bytes.Add((byte)(_easterEggActive.TryGetValue(sender.Uuid, out bool egg) && egg ? 1 : 0));
            bytes.AddRange(Encoding.UTF8.GetBytes(sender.DisplayName!));
        });
        sender.InfoPacket = infoPacket;

        if (firstReceive)
        {
            // Notify existing clients that this player has joined
            host.Broadcast(
                PacketBuilder.Build(CoreServerToClientOpcode.PlayerJoined, new[] { sender.Uuid }),
                PacketDelivery.ReliableOrdered, sender.PeerId);
        }

        host.Broadcast(infoPacket, PacketDelivery.ReliableOrdered, sender.PeerId);
    }

    private void HandleWorldEvent(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        if (data.Length < 2) return;
        ushort seq = BitConverter.ToUInt16(data, 0);
        if (!host.IsNewerSequence(sender.Uuid, seq)) return;

        var packet = PacketBuilder.Build(CoreServerToClientOpcode.WorldEvent, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.AddRange(data);
        });
        host.EnqueueHighPriority(sender.PeerId, packet);
    }

    private void HandleAccessoryAdd(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        if (data.Length < 1) return;
        byte accessoryType = data[0];

        var packet = PacketBuilder.Build(CoreServerToClientOpcode.AccessoryAdd, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.AddRange(data);
        });

        sender.SavedPackets[accessoryType] = packet;
        host.Broadcast(packet, PacketDelivery.ReliableOrdered, sender.PeerId);
    }

    private void HandleAccessoryRemove(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        if (data.Length < 1) return;
        byte accessoryType = data[0];

        var packet = PacketBuilder.Build(CoreServerToClientOpcode.AccessoryRemove, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.AddRange(data);
        });

        sender.SavedPackets[accessoryType] = null;
        host.Broadcast(packet, PacketDelivery.ReliableOrdered, sender.PeerId);
    }

    private void HandleJiminyUpdate(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        // data (opcode already stripped): [bool_state]
        if (data.Length < 1) return;
        sender.JiminyState = data[0] != 0;

        var packet = new byte[] {
            (byte)CoreServerToClientOpcode.JiminyRibbon,
            sender.Uuid,
            Convert.ToByte(sender.JiminyState)
        };

        sender.SavedPackets[0x02] = packet;
        host.Broadcast(packet, PacketDelivery.ReliableOrdered, sender.PeerId);
    }

    private void HandleCollisionToggle(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        if (data.Length < 1) return;
        sender.CollisionsEnabled = data[0] != 0;

        var packet = new byte[] {
            (byte)CoreServerToClientOpcode.CollisionToggle,
            sender.Uuid,
            Convert.ToByte(sender.CollisionsEnabled)
        };

        sender.SavedPackets[0x03] = packet;
        host.Broadcast(packet, PacketDelivery.ReliableOrdered, sender.PeerId);
    }

    private void HandleChatMessage(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        if (data.Length < 1) return;
        string message = Encoding.UTF8.GetString(data, 0, data.Length);
        Console.WriteLine($"{sender.DisplayName}[{sender.Uuid}]: {message}");

        var packet = PacketBuilder.Build(CoreServerToClientOpcode.ChatMessage, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.AddRange(data);
        });
        host.Broadcast(packet, PacketDelivery.ReliableOrdered, sender.PeerId);
    }

    private void HandleAudioFrame(ConnectedClient sender, byte[] data, NetworkHost host)
    {
        if (!_serverSettings.VoiceChatEnabled) return;

        var packet = PacketBuilder.Build(CoreServerToClientOpcode.AudioFrame, bytes =>
        {
            bytes.Add(sender.Uuid);
            bytes.AddRange(data);
        });
        host.EnqueueAudio(sender.PeerId, packet);
    }

    // --- Update check ---

    private void CheckForUpdates()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "BabyStepsServer");
            client.Timeout = TimeSpan.FromSeconds(5);

            var resp = client.GetAsync("https://api.github.com/repos/caleborchard/Baby-Steps-Multiplayer-Mod-Server/releases/latest").Result;
            if (!resp.IsSuccessStatusCode) return;

            var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v').Replace(".", "");

            if (tag != null && int.TryParse(SERVER_VERSION, out int cur) && int.TryParse(tag, out int latest))
            {
                if (cur < latest)
                {
                    Console.WriteLine("╔══════════════════════════════════════╗");
                    Console.WriteLine($"║  Server OUTDATED: {SERVER_VERSION} → {tag}        ║");
                    Console.WriteLine($"║  {GITHUB_REPO.Substring(8)}  ║");
                    Console.WriteLine("╚══════════════════════════════════════╝");
                }
                else
                    Console.WriteLine($"Server is up to date (v{SERVER_VERSION})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to check for updates: {ex.Message}");
        }
    }
}
