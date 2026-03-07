using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BabyStepsMultiplayerServer
{
    public class ServerSettings
    {
        public int Port { get; set; } = 7777;
        public string Password { get; set; } = "cuzzillobochfoddy";
        public int DistanceCutoff { get; set; } = 10;
        public int OuterDistanceCutoff { get; set; } = 500;
        public float StaticUpdateRate { get; set; } = 1000f;
        public float MaxBandwidthKbps { get; set; } = 512f; // Default ~512 KB/s
        public bool TelemetryEnabled { get; set; } = false;
        public float TelemetryUpdateInterval { get; set; } = 5000f; // ms
        public bool VoiceChatEnabled { get; set; } = true;
        public string DiscordWebhookUrl { get; set; } = "";
        public bool DiscordWebhookEnabled { get; set; } = false;

        private const string CONFIG_PATH = "settings.cfg";

        public static ServerSettings Load(string[]? launchArgs = null)
        {
            var settings = new ServerSettings();

            if (File.Exists(CONFIG_PATH))
            {
                foreach (var line in File.ReadAllLines(CONFIG_PATH))
                {
                    if (TryGetKeyValue(line, out string key, out string value))
                    {
                        settings.ApplySetting(key, value, "config");
                    }
                }
            }
            else
            {
                settings.CreateDefaultConfig();
                Console.WriteLine("No settings.cfg file found, creating default one");
            }

            settings.ApplyLaunchOverrides(launchArgs);

            return settings;
        }

        private static bool TryGetKeyValue(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                return false;

            key = line.Substring(0, equalsIndex);
            value = line.Substring(equalsIndex + 1);
            return true;
        }

        private void ApplyLaunchOverrides(string[]? launchArgs)
        {
            if (launchArgs == null || launchArgs.Length == 0)
                return;

            for (int i = 0; i < launchArgs.Length; i++)
            {
                var arg = launchArgs[i];
                if (!arg.StartsWith("--"))
                    continue;

                string key;
                string value;

                int equalsIndex = arg.IndexOf('=');
                if (equalsIndex > 2)
                {
                    key = arg.Substring(2, equalsIndex - 2);
                    value = arg.Substring(equalsIndex + 1);
                }
                else
                {
                    if (i + 1 >= launchArgs.Length || launchArgs[i + 1].StartsWith("--"))
                        continue;

                    key = arg.Substring(2);
                    value = launchArgs[++i];
                }

                value = TrimWrappingQuotes(value);
                ApplySetting(key, value, "launch options");
            }
        }

        private static string TrimWrappingQuotes(string value)
        {
            if (value.Length >= 2)
            {
                if ((value.StartsWith('"') && value.EndsWith('"')) ||
                    (value.StartsWith('\'') && value.EndsWith('\'')))
                {
                    return value.Substring(1, value.Length - 2);
                }
            }

            return value;
        }

        private void ApplySetting(string key, string value, string source)
        {
            if (key == "port")
            {
                if (int.TryParse(value, out int parsedPort))
                {
                    if (parsedPort > 0 && parsedPort < 65535)
                        Port = parsedPort;
                    else
                    {
                        Console.WriteLine($"Invalid port in {source}!");
                        Environment.Exit(0);
                    }
                }
            }
            else if (key == "password")
            {
                if (value.Length > 0)
                    Password = value;
            }
            else if (key == "player_transmit_cutoff")
            {
                if (int.TryParse(value, out int cutoff))
                {
                    if (cutoff > 0)
                        DistanceCutoff = cutoff;
                    else
                    {
                        Console.WriteLine($"Invalid Player Transmit Cutoff value in {source}");
                        Environment.Exit(0);
                    }
                }
            }
            else if (key == "outer_player_transmit_cutoff")
            {
                if (int.TryParse(value, out int cutoff))
                {
                    if (cutoff > 0)
                        OuterDistanceCutoff = cutoff;
                    else
                    {
                        Console.WriteLine($"Invalid Outer Player Transmit Cutoff value in {source}");
                        Environment.Exit(0);
                    }
                }
            }
            else if (key == "static_update_rate")
            {
                if (float.TryParse(value, out float rate))
                {
                    if (rate > 0)
                        StaticUpdateRate = rate;
                    else
                    {
                        Console.WriteLine($"Invalid Static Update Rate value in {source}");
                        Environment.Exit(0);
                    }
                }
            }
            else if (key == "max_bandwidth_kbps")
            {
                if (float.TryParse(value, out float bw))
                {
                    if (bw > 0)
                        MaxBandwidthKbps = bw;
                    else
                    {
                        Console.WriteLine($"Invalid Max Bandwidth value in {source}");
                        Environment.Exit(0);
                    }
                }
            }
            else if (key == "telemetry_enabled")
            {
                if (bool.TryParse(value, out bool enabled))
                    TelemetryEnabled = enabled;
            }
            else if (key == "telemetry_update_interval")
            {
                if (float.TryParse(value, out float interval))
                {
                    if (interval > 0)
                        TelemetryUpdateInterval = interval;
                }
            }
            else if (key == "voice_chat_enabled")
            {
                if (bool.TryParse(value, out bool vcEnabled))
                    VoiceChatEnabled = vcEnabled;
            }
            else if (key == "discord_webhook_url")
            {
                DiscordWebhookUrl = value;
            }
            else if (key == "discord_webhook_enabled")
            {
                if (bool.TryParse(value, out bool webhookEnabled))
                    DiscordWebhookEnabled = webhookEnabled;
            }
        }

        private void CreateDefaultConfig()
        {
            string[] lines =
            {
                "port=7777",
                "password=",
                "player_transmit_cutoff=10",
                "outer_player_transmit_cutoff=500",
                "static_update_rate=1000",
                "max_bandwidth_kbps=512",
                "telemetry_enabled=false",
                "telemetry_update_interval=5000",
                "voice_chat_enabled=true",
                "discord_webhook_url=",
                "discord_webhook_enabled=false"
            };
            File.WriteAllLines(CONFIG_PATH, lines);
        }
    }
}