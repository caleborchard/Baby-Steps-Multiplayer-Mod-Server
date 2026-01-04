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

        public async Task SendPlayerJoinedAsync(string username, byte uuid)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string message = $"Player **{username}**[{uuid}] joined";
            await SendMessageAsync(message, 0x00FF00); // Green color
        }

        public async Task SendPlayerLeftAsync(string username, byte uuid)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string message = $"Player **{username}**[{uuid}] left";
            await SendMessageAsync(message, 0xFF0000); // Red color
        }

        public async Task SendPlayerNameChangedAsync(string oldUsername, string newUsername, byte uuid)
        {
            if (!_enabled || string.IsNullOrWhiteSpace(_webhookUrl))
                return;

            string message = $"Player **{oldUsername}**[{uuid}] changed their name to **{newUsername}**[{uuid}]";
            await SendMessageAsync(message, 0xFFFF00); // Yellow color
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