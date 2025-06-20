using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Utilities;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
//using static Org.BouncyCastle.Bcpg.Attr.ImageAttrib;
using static System.Net.WebRequestMethods;

namespace PublishBlogWordpress
{
    public class ImagenService
    {
        readonly HttpClient _openAI;
        readonly HttpClient _wp;
        readonly string _apiKey;

        public ImagenService(IHttpClientFactory factory,
            IOptions<OpenAISettings> openAiOpts,
            IOptions<WordPressSettings> wpOpts)
        {
            _apiKey = openAiOpts.Value.ApiKey;

            _openAI = factory.CreateClient();
            _openAI.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _apiKey);

            var cfg = wpOpts.Value;
            _wp = factory.CreateClient();
            _wp.BaseAddress = new Uri(cfg.SiteUrl.TrimEnd('/'));
            _wp.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; WPClient/1.0)");
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.AppPassword}"));
            _wp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        public async Task<string> GenerateAndUploadAsync(string prompt, string filename)
        {
            // 1. Llama a OpenAI para generar la imagen
            var body = new { prompt, n = 1, size = "512x512" };
            var response = await _openAI.PostAsJsonAsync("https://api.openai.com/v1/images/generations", body);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new ApplicationException($"OpenAI image error {response.StatusCode}: {json}");

            var url = JsonDocument.Parse(json).RootElement.GetProperty("data")[0].GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(url))
                throw new ApplicationException("La URL de la imagen generada está vacía.");

            // 2. Descargar la imagen
            using var httpNoAuth = new HttpClient();
            var imageBytes = await httpNoAuth.GetByteArrayAsync(url);

            // 3. Crear multipart/form-data correctamente con el campo `file`
            var multipartContent = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipartContent.Add(fileContent, "file", $"{filename}.png");

            // 4. POST al endpoint de medios
            var uploadResponse = await _wp.PostAsync("/wp-json/wp/v2/media", multipartContent);
            var uploadJson = await uploadResponse.Content.ReadAsStringAsync();

            if (!uploadResponse.IsSuccessStatusCode)
                throw new ApplicationException($"Error al subir la imagen: {uploadResponse.StatusCode} - {uploadJson}");

            var doc = JsonDocument.Parse(uploadJson);
            return doc.RootElement.GetProperty("source_url").GetString()!;
        }
    }
}
