using System;
using System.IO;
using System.Text.Json;

namespace BabyStepsMultiplayerServer
{
    public class ServerStartupInfo
    {
        public TimeSpan? Downtime { get; set; }
        public bool PreviousRunLikelyCrashed { get; set; }
    }

    public class ServerLifecycleTracker
    {
        private const string STATE_PATH = "server_lifecycle_state.json";
        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly object _lock = new object();

        public ServerStartupInfo MarkServerStarted()
        {
            var now = DateTime.UtcNow;
            var state = ReadState();
            var startupInfo = BuildStartupInfo(state, now);

            state.IsRunning = true;
            state.LastHeartbeatUtc = now;
            state.LastStartupUtc = now;
            WriteState(state);

            return startupInfo;
        }

        public void UpdateHeartbeat()
        {
            var state = ReadState();
            state.IsRunning = true;
            state.LastHeartbeatUtc = DateTime.UtcNow;
            WriteState(state);
        }

        public void MarkCleanShutdown()
        {
            var now = DateTime.UtcNow;
            var state = ReadState();
            state.IsRunning = false;
            state.LastHeartbeatUtc = now;
            state.LastShutdownUtc = now;
            WriteState(state);
        }

        private ServerStartupInfo BuildStartupInfo(ServerLifecycleState state, DateTime now)
        {
            var result = new ServerStartupInfo();

            if (state.IsRunning && state.LastHeartbeatUtc.HasValue)
            {
                result.PreviousRunLikelyCrashed = true;
                result.Downtime = now - state.LastHeartbeatUtc.Value;
                return result;
            }

            if (state.LastShutdownUtc.HasValue)
            {
                result.Downtime = now - state.LastShutdownUtc.Value;
                return result;
            }

            if (state.LastHeartbeatUtc.HasValue)
            {
                result.Downtime = now - state.LastHeartbeatUtc.Value;
                return result;
            }

            result.Downtime = null;
            return result;
        }

        private ServerLifecycleState ReadState()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(STATE_PATH))
                        return new ServerLifecycleState();

                    var json = File.ReadAllText(STATE_PATH);
                    if (string.IsNullOrWhiteSpace(json))
                        return new ServerLifecycleState();

                    return JsonSerializer.Deserialize<ServerLifecycleState>(json) ?? new ServerLifecycleState();
                }
                catch
                {
                    return new ServerLifecycleState();
                }
            }
        }

        private void WriteState(ServerLifecycleState state)
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(state, _serializerOptions);
                    File.WriteAllText(STATE_PATH, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to update server lifecycle state: {ex.Message}");
                }
            }
        }

        private class ServerLifecycleState
        {
            public DateTime? LastStartupUtc { get; set; }
            public DateTime? LastHeartbeatUtc { get; set; }
            public DateTime? LastShutdownUtc { get; set; }
            public bool IsRunning { get; set; }
        }
    }
}
