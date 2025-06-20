using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublishBlogWordpress
{
    public class OpenAISettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
    }

    public class ChatGptService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _model;

        public ChatGptService(IHttpClientFactory factory, IOptions<OpenAISettings> opts)
        {
            _http = factory.CreateClient();
            var cfg = opts.Value;
            _apiKey = cfg.ApiKey;
            _model = cfg.Model;
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<List<string>> GetTrendingTopicsAsync()
        {
            var prompt = "Dame 3 temas en tendencia en twitter ahora llamado X (REGION MEXICO), solo un JSON array de strings sin nada más.";

            var body = new
            {
                model = _model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 200
            };

            var resp = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body);
            var contentJson = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"OpenAI error {resp.StatusCode}: {contentJson}");

            using var doc = JsonDocument.Parse(contentJson);
            var raw = doc.RootElement
                         .GetProperty("choices")[0]
                         .GetProperty("message")
                         .GetProperty("content")
                         .GetString() ?? "";

            // Extraer solo el JSON array [...]
            var start = raw.IndexOf('[');
            var end = raw.LastIndexOf(']');
            if (start < 0 || end < 0 || end <= start)
                throw new ApplicationException("No se encontró un array JSON en la respuesta de ChatGPT.");

            var jsonArray = raw[start..(end + 1)];
            return JsonSerializer.Deserialize<List<string>>(jsonArray)!;
        }

        public async Task<GeneratedPost> GeneratePostAsync(string topic)
        {
            var prompt = $@"
Eres un experto redactor SEO. Para el tema ""{topic}"":
1. Sugiéreme una categoría única.
2. Dame 5 tags SEO.
3. Crea un título llamativo de máximo 70 caracteres.
4. Redacta un artículo de ~500 palabras en HTML.

RESPONDE ÚNICAMENTE con un objeto JSON con estas llaves:
{{
  ""category"": string,
  ""tags"": [string],
  ""title"": string,
  ""content"": string
}}
Sin explicación adicional, sin comillas triples ni texto fuera del JSON.
";

            var body = new
            {
                model = _model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 1000
            };

            var resp = await _http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body);
            var contentJson = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"OpenAI error {resp.StatusCode}: {contentJson}");

            using var doc = JsonDocument.Parse(contentJson);
            var raw = doc.RootElement
                         .GetProperty("choices")[0]
                         .GetProperty("message")
                         .GetProperty("content")
                         .GetString() ?? "";

            // Extraer objeto JSON { ... }
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                throw new ApplicationException("No se encontró un objeto JSON en la respuesta de ChatGPT.");

            var jsonObject = raw[start..(end + 1)];
            return JsonSerializer.Deserialize<GeneratedPost>(jsonObject,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
    }

    public class GeneratedPost
    {
        public string Category { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
