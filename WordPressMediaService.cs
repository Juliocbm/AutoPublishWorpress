using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace PdfTutorialsFree.Services
{
    public record OpenAIOptions(string ApiKey, string Endpoint = "https://api.openai.com/v1/images/generations");
    public record WordPressOptions(string BaseUrl, string Username, string AppPassword);

    public interface IWordPressMediaService
    {
        /// <summary>
        /// Genera una imagen con OpenAI a partir del <paramref name="prompt"/> y la sube a WordPress.
        /// </summary>
        /// <param name="prompt">Prompt para generación.</param>
        /// <param name="filenameSlug">Nombre base (sin extensión) para el archivo en WP.</param>
        /// <param name="size">Tamaño de la imagen en formato "NxN" (p. ej. "512x512").</param>
        /// <returns>URL absoluta de la imagen subida en WordPress.</returns>
        Task<string> GenerateAndUploadAsync(string prompt, string filenameSlug, string size = "512x512");
    }

    /// <summary>
    /// Implementación resumida que usa IHttpClientFactory para gestionar clientes:
    /// * "openai"   -> headers con Bearer <ApiKey>
    /// * "wordpress"-> headers con autenticación Basic (usuario:appPassword)
    /// </summary>
    public sealed class WordPressMediaService : IWordPressMediaService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OpenAIOptions _openAIOptions;
        private readonly WordPressOptions _wpOptions;
        private readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

        public WordPressMediaService(IHttpClientFactory httpClientFactory,
                                     IOptions<OpenAIOptions> openAIOptions,
                                     IOptions<WordPressOptions> wpOptions)
        {
            _httpClientFactory = httpClientFactory;
            _openAIOptions = openAIOptions.Value;
            _wpOptions = wpOptions.Value;
        }

        public async Task<string> GenerateAndUploadAsync(string prompt, string filenameSlug, string size = "512x512")
        {
            // 1. Generar la imagen con OpenAI
            var openAiClient = _httpClientFactory.CreateClient("openai");

            var body = JsonSerializer.Serialize(new { prompt, n = 1, size }, _jsonOpts);
            using var resp = await openAiClient.PostAsync(_openAIOptions.Endpoint,
                                                          new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"OpenAI image error {resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var url = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(url))
                throw new ApplicationException("OpenAI image URL missing");

            // 2. Descargar imagen sin headers especiales
            var httpNoAuth = _httpClientFactory.CreateClient(); // cliente limpio
            var imageBytes = await httpNoAuth.GetByteArrayAsync(url);

            // 3. Preparar multipart/form-data
            using var multipart = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(fileContent, "file", $"{filenameSlug}.png");

            // 4. Subir al endpoint WordPress
            var wpClient = _httpClientFactory.CreateClient("wordpress");
            using var uploadResp = await wpClient.PostAsync("/wp-json/wp/v2/media", multipart);
            var uploadBody = await uploadResp.Content.ReadAsStringAsync();
            if (!uploadResp.IsSuccessStatusCode)
                throw new ApplicationException($"WordPress upload error {uploadResp.StatusCode}: {uploadBody}");

            using var docUpload = JsonDocument.Parse(uploadBody);
            return docUpload.RootElement.GetProperty("source_url").GetString() ?? throw new ApplicationException("WP response missing source_url");
        }
    }

    #region HttpClient registration helpers

    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Agrega los HttpClient nombrados "openai" y "wordpress" + el servicio <see cref="WordPressMediaService"/>
        /// </summary>
        public static IServiceCollection AddWordPressMedia(this IServiceCollection services,
                                                           Action<OpenAIOptions> openAI,
                                                           Action<WordPressOptions> wordpress)
        {
            services.Configure(openAI);
            services.Configure(wordpress);

            services.AddHttpClient("openai", (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            });

            services.AddHttpClient("wordpress", (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<WordPressOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.Username}:{opts.AppPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.AddTransient<IWordPressMediaService, WordPressMediaService>();
            return services;
        }
    }

    #endregion
}
