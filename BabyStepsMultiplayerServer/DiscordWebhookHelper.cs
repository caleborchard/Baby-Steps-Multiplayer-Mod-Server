using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BabyStepsMultiplayerServer
{
    public class DiscordWebhookHelper
    {
        private readonly string _webhookUrl;
        private readonly bool _enabled;
        private readonly HttpClient _httpClient;

        public DiscordWebhookHelper(string webhookUrl, bool enabled)
        {
            _webhookUrl = webhookUrl;
            _enabled = enabled;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task SendServerStartedAsync(TimeSpan? downtime, bool previousRunLikelyCrashed)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string downtimeText = downtime.HasValue ? FormatDuration(downtime.Value) : "Unknown";
            string crashText = previousRunLikelyCrashed ? "\nPrevious run likely ended unexpectedly." : string.Empty;
            string message = $"Server started.\nDowntime: **{downtimeText}**{crashText}";
            await SendMessageAsync(message, 0x0099FF);
        }

        public async Task SendPlayerJoinedAsync(string username, byte uuid, int currentPlayerCount)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string message = $"Player **{username}**[{uuid}] joined\nPlayer count: **{currentPlayerCount}**";
            await SendMessageAsync(message, 0x00FF00);
        }

        public async Task SendPlayerLeftAsync(string username, byte uuid, int currentPlayerCount)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string message = $"Player **{username}**[{uuid}] left\nPlayer count: **{currentPlayerCount}**";
            await SendMessageAsync(message, 0xFF0000);
        }

        public async Task SendPlayerNameChangedAsync(string oldUsername, string newUsername, byte uuid)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string message = $"Player **{oldUsername}**[{uuid}] changed their name to **{newUsername}**[{uuid}]";
            await SendMessageAsync(message, 0xFFFF00); // Yellow color
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m {duration.Seconds}s";

            if (duration.TotalHours >= 1)
                return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";

            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}m {duration.Seconds}s";

            return $"{Math.Max(0, duration.Seconds)}s";
        }

        private async Task SendMessageAsync(string message, int color)
        {
            try
            {
                var payload = new
                {
                    embeds = new[]
                    {
                        new
                        {
                            description = message,
                            color = color,
                            timestamp = DateTime.UtcNow.ToString("o")
                        }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_webhookUrl, content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Failed to send Discord webhook: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending Discord webhook: {ex.Message}");
            }
        }
    }
}