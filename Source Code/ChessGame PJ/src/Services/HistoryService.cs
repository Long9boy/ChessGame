using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChessGame_PJ.Core;

namespace ChessGame_PJ.Services
{
    public static class HistoryService
    {
        // Maximum number of match records kept per player
        private const int MaxHistoryPerUser = 10;

        // Save game record for both participants and prune older matches
        public static async Task SaveGameRecordAsync(GameRecord record)
        {
            if (record == null) return;
            if (string.IsNullOrEmpty(record.gameId))
                record.gameId = Guid.NewGuid().ToString("N");
            if (record.timestamp <= 0)
                record.timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            bool IsBot(string? name) => string.IsNullOrWhiteSpace(name) ||
                name.StartsWith("bot_", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("máy", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("stockfish", StringComparison.OrdinalIgnoreCase);

            var playersToSave = new List<string>();
            if (!IsBot(record.playerWhite))
                playersToSave.Add(record.playerWhite.Trim().ToLowerInvariant());
            if (!IsBot(record.playerBlack))
            {
                string b = record.playerBlack!.Trim().ToLowerInvariant();
                if (!playersToSave.Contains(b))
                    playersToSave.Add(b);
            }

            foreach (var user in playersToSave)
            {
                try
                {
                    string path = $"game_history/{user}/{record.gameId}";
                    await FirebaseClient.PutAsync(path, record);

                    // Prune oldest records if exceeding MaxHistoryPerUser
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var history = await FirebaseClient.GetAsync<Dictionary<string, GameRecord>>($"game_history/{user}");
                            if (history != null && history.Count > MaxHistoryPerUser)
                            {
                                var excess = history
                                    .OrderBy(kv => kv.Value?.timestamp ?? 0)
                                    .Take(history.Count - MaxHistoryPerUser)
                                    .ToList();

                                foreach (var oldGame in excess)
                                {
                                    await FirebaseClient.DeleteAsync($"game_history/{user}/{oldGame.Key}");
                                }
                            }
                        }
                        catch { }
                    });
                }
                catch { }
            }
        }

        // Get recent matches for a user
        public static async Task<List<GameRecord>> GetUserHistoryAsync(string username, int limit = 10)
        {
            var result = new List<GameRecord>();
            if (string.IsNullOrWhiteSpace(username)) return result;

            try
            {
                var dict = await FirebaseClient.GetAsync<Dictionary<string, GameRecord>>($"game_history/{username.Trim().ToLowerInvariant()}");
                if (dict == null || dict.Count == 0) return result;

                return dict.Values
                    .Where(g => g != null)
                    .OrderByDescending(g => g.timestamp)
                    .Take(limit)
                    .ToList();
            }
            catch
            {
                return result;
            }
        }
    }
}
