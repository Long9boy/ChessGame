using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChessGame_PJ
{
    // Communicates with Firebase Realtime Database via REST API (no SDK/NuGet required).
    // Requires Realtime Database Rules to allow read/write (test mode) because this
    // REST API does not include an auth token and uses the database URL directly.
    public static class FirebaseClient
    {
        private static readonly HttpClient http = new HttpClient();

        // firebaseConfig.databaseURL
        private const string DatabaseUrl = "https://duaxe-31db1-default-rtdb.asia-southeast1.firebasedatabase.app";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<T?> GetAsync<T>(string path)
        {
            try
            {
                string url = $"{DatabaseUrl}/{path}.json";
                HttpResponseMessage res = await http.GetAsync(url);
                if (!res.IsSuccessStatusCode) return default;

                string json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json) || json == "null") return default;

                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch
            {
                return default;
            }
        }

        public static async Task<bool> PutAsync<T>(string path, T value)
        {
            try
            {
                string url = $"{DatabaseUrl}/{path}.json";
                string json = JsonSerializer.Serialize(value, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage res = await http.PutAsync(url, content);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> PatchAsync(string path, object value)
        {
            try
            {
                string url = $"{DatabaseUrl}/{path}.json";
                string json = JsonSerializer.Serialize(value, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
                HttpResponseMessage res = await http.SendAsync(req);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> DeleteAsync(string path)
        {
            try
            {
                string url = $"{DatabaseUrl}/{path}.json";
                HttpResponseMessage res = await http.DeleteAsync(url);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
