using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;


namespace PublishBlogWordpress
{

    public class PdfService
    {
        private readonly HttpClient _http;
        public PdfService(IHttpClientFactory factory, IOptions<WordPressSettings> opts)
        {
            var cfg = opts.Value;
            _http = factory.CreateClient();
            _http.BaseAddress = new Uri(cfg.SiteUrl.TrimEnd('/'));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; WPClient/1.0)");
            var token = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.AppPassword}")
            );
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }

        /// <summary>
        /// Genera un PDF con contenido + banner de texto y lo sube como media. Devuelve la URL pública.
        /// </summary>
        public async Task<string> GenerateAndUploadPdfAsync(string htmlContent, string filename)
        {
            // 1) Crear PDF en memoria
            using var ms = new MemoryStream();
            var writer = new PdfWriter(ms);
            var pdfDoc = new PdfDocument(writer);
            var doc = new Document(pdfDoc);

            // Título
            doc.Add(new Paragraph("Contenido del Post").SetBold().SetFontSize(16));
            // Cuerpo (simple)
            doc.Add(new Paragraph(htmlContent));
            // Banner de texto simulando anuncio
            doc.Add(new Paragraph("\n📢 Patrocinado por AcmeAds: visita acmeads.com para más información.\n"));

            doc.Close();
            ms.Position = 0;

            // 2) Preparar petición para /media
            var uploadReq = new HttpRequestMessage(HttpMethod.Post, "/wp-json/wp/v2/media");
            uploadReq.Content = new StreamContent(ms);
            uploadReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            uploadReq.Headers.Accept.Clear();
            uploadReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            uploadReq.Headers.Add("Content-Disposition", $"attachment; filename=\"{filename}.pdf\"");

            // 3) Enviar y parsear respuesta
            var resp = await _http.SendAsync(uploadReq);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"Error subiendo PDF: {resp.StatusCode} {body}");

            using var docJson = JsonDocument.Parse(body);
            return docJson.RootElement.GetProperty("source_url").GetString()!;
        }
    }

}
