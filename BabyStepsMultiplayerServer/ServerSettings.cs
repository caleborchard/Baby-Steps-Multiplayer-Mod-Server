using BabyStepsNetworking.Host;

namespace BabyStepsMultiplayerServer;

public class ServerSettings
{
    public int Port { get; set; } = 7777;
    public string Password { get; set; } = "cuzzillobochfoddy";
    public int DistanceCutoff { get; set; } = 10;
    public int OuterDistanceCutoff { get; set; } = 500;
    public float StaticUpdateRate { get; set; } = 1000f;
    public float MaxBandwidthKbps { get; set; } = 512f;
    public bool TelemetryEnabled { get; set; } = false;
    public float TelemetryUpdateInterval { get; set; } = 5000f;
    public bool VoiceChatEnabled { get; set; } = true;
    public string DiscordWebhookUrl { get; set; } = "";
    public bool DiscordWebhookEnabled { get; set; } = false;

    private const string CONFIG_PATH = "settings.cfg";

    public HostSettings ToHostSettings(string serverVersion) => new()
    {
        Port = Port,
        Password = Password,
        DistanceCutoff = DistanceCutoff,
        OuterDistanceCutoff = OuterDistanceCutoff,
        CullIntervalMs = StaticUpdateRate,
        MaxBandwidthKbps = MaxBandwidthKbps,
        TelemetryEnabled = TelemetryEnabled,
        TelemetryIntervalMs = TelemetryUpdateInterval,
        VoiceChatEnabled = VoiceChatEnabled,
        ServerVersion = serverVersion,
    };

    public static ServerSettings Load(string[]? launchArgs = null)
    {
        var s = new ServerSettings();

        if (File.Exists(CONFIG_PATH))
        {
            foreach (var line in File.ReadAllLines(CONFIG_PATH))
                if (TryGetKeyValue(line, out string key, out string value))
                    s.ApplySetting(key, value, "config");
        }
        else
        {
            s.CreateDefaultConfig();
            Console.WriteLine("No settings.cfg found, creating default.");
        }

        s.ApplyLaunchOverrides(launchArgs);
        return s;
    }

    private static bool TryGetKeyValue(string line, out string key, out string value)
    {
        key = string.Empty; value = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;
        int eq = line.IndexOf('=');
        if (eq <= 0) return false;
        key = line[..eq];
        value = line[(eq + 1)..];
        return true;
    }

    private void ApplyLaunchOverrides(string[]? args)
    {
        if (args == null) return;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            string key, value;
            int eq = args[i].IndexOf('=');
            if (eq > 2) { key = args[i][2..eq]; value = args[i][(eq + 1)..]; }
            else
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) continue;
                key = args[i][2..]; value = args[++i];
            }
            ApplySetting(key, TrimQuotes(value), "launch args");
        }
    }

    private static string TrimQuotes(string v)
        => v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\''))
            ? v[1..^1] : v;

    private void ApplySetting(string key, string value, string source)
    {
        switch (key)
        {
            case "port":
                if (int.TryParse(value, out int p) && p > 0 && p < 65535) Port = p;
                else { Console.WriteLine($"Invalid port in {source}!"); Environment.Exit(0); }
                break;
            case "password":
                Password = value.Length > 0 ? value : "cuzzillobochfoddy";
                break;
            case "player_transmit_cutoff":
                if (int.TryParse(value, out int d) && d > 0) DistanceCutoff = d;
                break;
            case "outer_player_transmit_cutoff":
                if (int.TryParse(value, out int od) && od > 0) OuterDistanceCutoff = od;
                break;
            case "static_update_rate":
                if (float.TryParse(value, out float r) && r > 0) StaticUpdateRate = r;
                break;
            case "max_bandwidth_kbps":
                if (float.TryParse(value, out float bw) && bw > 0) MaxBandwidthKbps = bw;
                break;
            case "telemetry_enabled":
                if (bool.TryParse(value, out bool t)) TelemetryEnabled = t;
                break;
            case "telemetry_update_interval":
                if (float.TryParse(value, out float ti) && ti > 0) TelemetryUpdateInterval = ti;
                break;
            case "voice_chat_enabled":
                if (bool.TryParse(value, out bool vc)) VoiceChatEnabled = vc;
                break;
            case "discord_webhook_url":
                DiscordWebhookUrl = value;
                break;
            case "discord_webhook_enabled":
                if (bool.TryParse(value, out bool dw)) DiscordWebhookEnabled = dw;
                break;
        }
    }

    private void CreateDefaultConfig()
    {
        File.WriteAllLines(CONFIG_PATH, new[]
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
            "discord_webhook_enabled=false",
        });
    }
}
