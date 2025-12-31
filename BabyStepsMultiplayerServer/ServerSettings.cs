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

        public string BotToken { get; set; } = "";

        private const string CONFIG_PATH = "settings.cfg";

        public static ServerSettings Load()
        {
            var settings = new ServerSettings();

            if (File.Exists(CONFIG_PATH))
            {
                foreach (var line in File.ReadAllLines(CONFIG_PATH))
                {
                    if (line.StartsWith("port="))
                    {
                        if (int.TryParse(line.Substring("port=".Length), out int parsedPort))
                        {
                            if (parsedPort > 0 && parsedPort < 65535)
                                settings.Port = parsedPort;
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
                            settings.Password = parsedPwd;
                    }
                    else if (line.StartsWith("player_transmit_cutoff="))
                    {
                        if (int.TryParse(line.Substring("player_transmit_cutoff=".Length), out int cutoff))
                        {
                            if (cutoff > 0)
                                settings.DistanceCutoff = cutoff;
                            else
                            {
                                Console.WriteLine("Invalid Player Transmit Cutoff value");
                                Environment.Exit(0);
                            }
                        }
                    }
                    else if (line.StartsWith("outer_player_transmit_cutoff="))
                    {
                        if (int.TryParse(line.Substring("outer_player_transmit_cutoff=".Length), out int cutoff))
                        {
                            if (cutoff > 0)
                                settings.OuterDistanceCutoff = cutoff;
                            else
                            {
                                Console.WriteLine("Invalid Outer Player Transmit Cutoff value");
                                Environment.Exit(0);
                            }
                        }
                    }
                    else if (line.StartsWith("static_update_rate="))
                    {
                        if (float.TryParse(line.Substring("static_update_rate=".Length), out float rate))
                        {
                            if (rate > 0)
                                settings.StaticUpdateRate = rate;
                            else
                            {
                                Console.WriteLine("Invalid Static Update Rate value");
                                Environment.Exit(0);
                            }
                        }
                    }
                    else if (line.StartsWith("max_bandwidth_kbps="))
                    {
                        if (float.TryParse(line.Substring("max_bandwidth_kbps=".Length), out float bw))
                        {
                            if (bw > 0)
                                settings.MaxBandwidthKbps = bw;
                            else
                            {
                                Console.WriteLine("Invalid Max Bandwidth value");
                                Environment.Exit(0);
                            }
                        }
                    }
                    else if (line.StartsWith("telemetry_enabled="))
                    {
                        if (bool.TryParse(line.Substring("telemetry_enabled=".Length), out bool enabled))
                            settings.TelemetryEnabled = enabled;
                    }
                    else if (line.StartsWith("telemetry_update_interval="))
                    {
                        if (float.TryParse(line.Substring("telemetry_update_interval=".Length), out float interval))
                        {
                            if (interval > 0)
                                settings.TelemetryUpdateInterval = interval;
                        }
                    }
                    else if (line.StartsWith("voice_chat_enabled="))
                    {
                        if (bool.TryParse(line.Substring("voice_chat_enabled=".Length), out bool vcEnabled))
                            settings.VoiceChatEnabled = vcEnabled;
                    }
                    else if (line.StartsWith("bot_token="))
                    {
                        string token = line.Substring("bot_token=".Length);
                        if(token.Length > 0)
                        {
                            settings.BotToken = token;
                        }
                        else
                        {
                            Console.WriteLine("No bot token provided, discord functionality disabled");
                        }
                    }
                }
            }
            else
            {
                settings.CreateDefaultConfig();
                Console.WriteLine("No settings.cfg file found, creating default one");
            }

            return settings;
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
                "bot_token=0"
            };
            File.WriteAllLines(CONFIG_PATH, lines);
        }
    }
}
