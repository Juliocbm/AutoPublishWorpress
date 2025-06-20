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
            var body = new
            {
                prompt = prompt,
                n = 1,
                size = "512x512"
            };

            var resp = await _openAI.PostAsJsonAsync("https://api.openai.com/v1/images/generations", body);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"OpenAI image error {resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var url = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
            if (string.IsNullOrEmpty(url))
                throw new ApplicationException("OpenAI image URL missing");

            using var httpNoAuth = new HttpClient();
            var imageBytes = await httpNoAuth.GetByteArrayAsync(url);

            var uploadReq = new HttpRequestMessage(HttpMethod.Post, "/wp-json/wp/v2/media");
            uploadReq.Content = new ByteArrayContent(imageBytes);
            uploadReq.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            // ✅ CORRECTO: Content-Disposition debe estar en Content.Headers
            uploadReq.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = $"{filename}.png"
            };

            uploadReq.Headers.Accept.Clear();
            uploadReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            var uploadResp = await _wp.SendAsync(uploadReq);
            var uploadBody = await uploadResp.Content.ReadAsStringAsync();
            if (!uploadResp.IsSuccessStatusCode)
                throw new ApplicationException($"Error subiendo imagen: {uploadResp.StatusCode} {uploadBody}"); //Error actual necesario revisar:{"code":"rest_upload_sideload_error","message":"Lo siento, no tienes permisos para subir este tipo de archivo.","data":{"status":500}}

            using var docUpload = JsonDocument.Parse(uploadBody);
            return docUpload.RootElement.GetProperty("source_url").GetString()!;
        }
    }
}
