using BabyStepsServer;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BabyStepsMultiplayerServer
{
    public class BandwidthManager
    {
        private enum PacketPriority
        {
            High,      // Chat, client info, world events, accessories, etc.
            Medium,    // Bone updates
            Low        // Audio frames
        }

        private class QueuedPacket
        {
            public required NetPeer FromPeer;
            public required byte[] Data;
            public required PacketPriority Priority;
            public long QueuedTime;
        }

        private readonly Queue<QueuedPacket> _highPriorityQueue = new();
        private readonly Queue<QueuedPacket> _mediumPriorityQueue = new();
        private readonly Queue<QueuedPacket> _lowPriorityQueue = new();

        private readonly ServerSettings _settings;
        private readonly int _targetFPS;
        private readonly Dictionary<NetPeer, ClientInfo> _clients;
        private readonly NetDataWriter _writer = new();

        // Audio proximity constant
        private const float AUDIO_MAX_DISTANCE = 60f; // Technically the falloff ends at 100, but by 60 you cannot hear anything.

        // Telemetry
        private long _bytesSentThisInterval = 0;
        private long _lastTelemetryTime = 0;
        private readonly Stopwatch _telemetryStopwatch = new();

        public BandwidthManager(ServerSettings settings, int targetFPS, Dictionary<NetPeer, ClientInfo> clients)
        {
            _settings = settings;
            _targetFPS = targetFPS;
            _clients = clients;
            _telemetryStopwatch.Start();
        }

        public void EnqueueBoneUpdate(NetPeer from, byte[] data)
        {
            _mediumPriorityQueue.Enqueue(new QueuedPacket
            {
                FromPeer = from,
                Data = data,
                Priority = PacketPriority.Medium,
                QueuedTime = Stopwatch.GetTimestamp()
            });
        }

        public void EnqueueAudioFrame(NetPeer from, byte[] data)
        {
            _lowPriorityQueue.Enqueue(new QueuedPacket
            {
                FromPeer = from,
                Data = data,
                Priority = PacketPriority.Low,
                QueuedTime = Stopwatch.GetTimestamp()
            });
        }

        public void EnqueueHighPriority(NetPeer from, byte[] data)
        {
            _highPriorityQueue.Enqueue(new QueuedPacket
            {
                FromPeer = from,
                Data = data,
                Priority = PacketPriority.High,
                QueuedTime = Stopwatch.GetTimestamp()
            });
        }

        public void ProcessQueues()
        {
            // Calculate bandwidth budget for this tick
            float bytesPerSecond = _settings.MaxBandwidthKbps * 1024f;
            float bytesPerTick = bytesPerSecond / _targetFPS;
            int byteBudget = (int)bytesPerTick;

            long now = Stopwatch.GetTimestamp();
            double tickToMs = 1000.0 / Stopwatch.Frequency;
            float updateIntervalMs = 1000f / _targetFPS;

            int bytesUsed = 0;

            // Process high priority first (always send these)
            while (_highPriorityQueue.Count > 0)
            {
                var packet = _highPriorityQueue.Dequeue();
                if (!_clients.ContainsKey(packet.FromPeer)) continue;

                int packetSize = BroadcastPacket(packet, DeliveryMethod.ReliableOrdered);
                bytesUsed += packetSize;
            }

            // Process medium priority (bone updates) with distance-based throttling
            while (_mediumPriorityQueue.Count > 0 && bytesUsed < byteBudget)
            {
                var packet = _mediumPriorityQueue.Dequeue();
                if (!_clients.ContainsKey(packet.FromPeer)) continue;

                int packetSize = BroadcastBoneUpdate(packet, now, tickToMs, updateIntervalMs);
                bytesUsed += packetSize;
            }

            // Process low priority (audio) with remaining bandwidth and proximity check
            while (_lowPriorityQueue.Count > 0 && bytesUsed < byteBudget)
            {
                var packet = _lowPriorityQueue.Dequeue();
                if (!_clients.ContainsKey(packet.FromPeer)) continue;

                int packetSize = BroadcastAudioFrame(packet);
                bytesUsed += packetSize;
            }

            // Update telemetry
            _bytesSentThisInterval += bytesUsed;
            UpdateTelemetry();
        }

        private int BroadcastBoneUpdate(QueuedPacket packet, long now, double tickToMs, float updateIntervalMs)
        {
            var fromClient = _clients[packet.FromPeer];
            _writer.Reset();
            ushort totalLength = (ushort)(packet.Data.Length + 2);
            _writer.Put(totalLength);
            _writer.Put(packet.Data);

            int totalBytes = 0;

            foreach (var kvp in _clients)
            {
                var peer = kvp.Key;
                if (peer == packet.FromPeer) continue;

                bool isDistant = fromClient.distantClients?.Contains(peer) == true;
                float effectiveMultiplier = 1f;

                if (isDistant)
                {
                    var targetClient = kvp.Value;
                    float distance = Vector3.Distance(fromClient.position, targetClient.position);

                    if (distance >= _settings.DistanceCutoff)
                    {
                        float clamped = Math.Clamp(
                            (distance - _settings.DistanceCutoff) / (_settings.OuterDistanceCutoff - _settings.DistanceCutoff),
                            0f, 1f
                        );

                        float targetMultiplier = updateIntervalMs / 2000f;
                        effectiveMultiplier = (float)Math.Exp(clamped * Math.Log(targetMultiplier));
                    }
                }

                float sendInterval = updateIntervalMs / effectiveMultiplier;

                if (!fromClient.lastTransmitTimes.TryGetValue(peer, out long lastSent))
                    lastSent = 0;

                double msSinceLastSend = (now - lastSent) * tickToMs;

                if (msSinceLastSend >= sendInterval)
                {
                    peer.Send(_writer, DeliveryMethod.Unreliable);
                    fromClient.lastTransmitTimes[peer] = now;
                    totalBytes += totalLength;
                }
            }

            return totalBytes;
        }

        private int BroadcastAudioFrame(QueuedPacket packet)
        {
            var fromClient = _clients[packet.FromPeer];
            _writer.Reset();
            ushort totalLength = (ushort)(packet.Data.Length + 2);
            _writer.Put(totalLength);
            _writer.Put(packet.Data);

            int totalBytes = 0;

            foreach (var kvp in _clients)
            {
                var peer = kvp.Key;
                if (peer == packet.FromPeer) continue;

                var targetClient = kvp.Value;
                float distance = Vector3.Distance(fromClient.position, targetClient.position);

                // Only send audio if within 100 units
                if (distance <= AUDIO_MAX_DISTANCE)
                {
                    peer.Send(_writer, DeliveryMethod.Unreliable);
                    totalBytes += totalLength;
                }
            }

            return totalBytes;
        }

        private int BroadcastPacket(QueuedPacket packet, DeliveryMethod deliveryMethod)
        {
            _writer.Reset();
            ushort totalLength = (ushort)(packet.Data.Length + 2);
            _writer.Put(totalLength);
            _writer.Put(packet.Data);

            int recipientCount = 0;
            foreach (var client in _clients)
            {
                if (client.Key == packet.FromPeer) continue;
                client.Key.Send(_writer, deliveryMethod);
                recipientCount++;
            }

            return totalLength * recipientCount;
        }

        private void UpdateTelemetry()
        {
            if (!_settings.TelemetryEnabled) return;

            long currentTime = _telemetryStopwatch.ElapsedMilliseconds;
            long elapsed = currentTime - _lastTelemetryTime;

            if (elapsed >= _settings.TelemetryUpdateInterval)
            {
                float secondsElapsed = elapsed / 1000f;
                float kbps = (_bytesSentThisInterval / 1024f) / secondsElapsed;
                float utilizationPercent = (kbps / _settings.MaxBandwidthKbps) * 100f;

                Console.WriteLine($"[TELEMETRY] Bandwidth: {kbps:F2} KB/s ({utilizationPercent:F1}% of {_settings.MaxBandwidthKbps} KB/s limit)");
                Console.WriteLine($"[TELEMETRY] Queue sizes - High: {_highPriorityQueue.Count}, Medium: {_mediumPriorityQueue.Count}, Low: {_lowPriorityQueue.Count}");

                _bytesSentThisInterval = 0;
                _lastTelemetryTime = currentTime;
            }
        }

        public int GetQueueSizes()
        {
            return _highPriorityQueue.Count + _mediumPriorityQueue.Count + _lowPriorityQueue.Count;
        }
    }
}