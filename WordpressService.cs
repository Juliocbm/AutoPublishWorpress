using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Document = iText.Layout.Document;

namespace PublishBlogWordpress
{
    //public record WordPressSettings(string SiteUrl, string Username, string AppPassword);

    public class WordPressSettings
    {
        // El binder necesita setter público para poder asignar los valores
        public string SiteUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string AppPassword { get; set; } = string.Empty;
    }


    public class WordPressService
    {
        readonly HttpClient _http;

        public WordPressService(IHttpClientFactory factory, IOptions<WordPressSettings> opts)
        {
            var cfg = opts.Value;
            _http = factory.CreateClient();

            // 1) BaseAddress a tu sitio
            _http.BaseAddress = new Uri(cfg.SiteUrl.TrimEnd('/'));

            // 2) User-Agent "genérico" para evitar bloqueos de Mod_Security
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; WPClient/1.0)");

            // 3) Auth básica con Application Password
            var token = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.AppPassword}")
            );
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", token);
        }

        public async Task<int> EnsureCategoryAsync(string name)
        {
            // 1) Intentar obtener la lista filtrada
            var searchUrl = $"/wp-json/wp/v2/categories?search={Uri.EscapeDataString(name)}";
            var getReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            getReq.Headers.Accept.Clear();
            getReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            var respList = await _http.SendAsync(getReq);
            var jsonList = await respList.Content.ReadAsStringAsync();

            if (!respList.IsSuccessStatusCode)
                throw new ApplicationException($"WP list categories error {respList.StatusCode}: {jsonList}");

            var list = JsonSerializer.Deserialize<List<WpTerm>>(jsonList,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new List<WpTerm>();

            var found = list.Find(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (found != null)
                return found.Id;

            // Si hay resultados de búsqueda, reutiliza la primera categoría encontrada
            if (list.Count > 0)
                return list[0].Id;

            // 2) Crear la categoría si no existe
            var payload = new { name };
            var postReq = new HttpRequestMessage(HttpMethod.Post, "/wp-json/wp/v2/categories")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                )
            };
            postReq.Headers.Accept.Clear();
            postReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            var respCreate = await _http.SendAsync(postReq);
            var jsonCreate = await respCreate.Content.ReadAsStringAsync();

            if (!respCreate.IsSuccessStatusCode)
                throw new ApplicationException($"WP create category error {respCreate.StatusCode}: {jsonCreate}");

            var created = JsonSerializer.Deserialize<WpTerm>(jsonCreate,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? throw new ApplicationException("Failed to parse created category.");

            return created.Id;
        }


        public async Task<List<int>> EnsureTagsAsync(IEnumerable<string> tags)
        {
            var ids = new List<int>();

            foreach (var tag in tags)
            {
                // 1) Buscar si existe el tag
                var searchUrl = $"/wp-json/wp/v2/tags?search={Uri.EscapeDataString(tag)}";
                var getReq = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                getReq.Headers.Accept.Clear();
                getReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

                var respList = await _http.SendAsync(getReq);
                var jsonList = await respList.Content.ReadAsStringAsync();

                if (!respList.IsSuccessStatusCode)
                    throw new ApplicationException($"WP list tags error {respList.StatusCode}: {jsonList}");

                var list = JsonSerializer.Deserialize<List<WpTerm>>(jsonList,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? new List<WpTerm>();

                var found = list.Find(t =>
                    t.Name.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    ids.Add(found.Id);
                    continue;
                }

                // 2) Crear el tag si no existe
                var payload = new { name = tag };
                var postReq = new HttpRequestMessage(HttpMethod.Post, "/wp-json/wp/v2/tags")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json"
                    )
                };
                postReq.Headers.Accept.Clear();
                postReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

                var respCreate = await _http.SendAsync(postReq);
                var jsonCreate = await respCreate.Content.ReadAsStringAsync();

                if (!respCreate.IsSuccessStatusCode)
                    throw new ApplicationException($"WP create tag error {respCreate.StatusCode}: {jsonCreate}");

                var created = JsonSerializer.Deserialize<WpTerm>(jsonCreate,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                              ?? throw new ApplicationException("Failed to parse created tag.");

                ids.Add(created.Id);
            }

            return ids;
        }

        /// <summary>
        /// Genera el PDF, construye el contenido enriquecido y publica el post.
        /// </summary>
        public async Task CreatePostAsync(GeneratedPost gp)
        {
            // 1) Categoría y tags
            var catId = await EnsureCategoryAsync(gp.Category);
            var tagIds = await EnsureTagsAsync(gp.Tags);

            // 2) Generar y subir PDF
            var slug = gp.Title.ToLowerInvariant().Replace(" ", "-");
            var pdfUrl = await GenerateAndUploadPdfAsync(gp.Content, slug);

            // 3) Contenido enriquecido (banner AdSense + contador)
            //var enhancedContent = $@"
            //    <!-- banner_inicio -->
            //    <ins class=""adsbygoogle""
            //         style=""display:block""
            //         data-ad-client=""ca-pub-9450730864089238""
            //         data-ad-slot=""5261285091""
            //         data-ad-format=""auto""
            //         data-full-width-responsive=""true""></ins>
            //    <script>
            //         (adsbygoogle = window.adsbygoogle || []).push({{}});
            //    </script>


            //    {gp.Content}

            //    <button id='dlBtn'>Descargar PDF</button>
            //    <script>
            //        document.getElementById('dlBtn').onclick = () => {{
            //            let c = 5;
            //            const btn = document.getElementById('dlBtn');
            //            const i = setInterval(() => {{
            //                if (c-- === 0) window.location.href = '{pdfUrl}';
            //                else btn.textContent = 'Descarga en ' + c + 's';
            //            }}, 1000);
            //        }};
            //    </script>";

            var enhancedContent = $@"
                {gp.Content}

                <button id='dlBtn'>Descargar PDF</button>
                <script>
                    document.getElementById('dlBtn').onclick = () => {{
                        let c = 5;
                        const btn = document.getElementById('dlBtn');
                        const i = setInterval(() => {{
                            if (c-- === 0) window.location.href = '{pdfUrl}';
                            else btn.textContent = 'Descarga en ' + c + 's';
                        }}, 1000);
                    }};
                </script>";

            // 4) Publicar post
            var postObj = new
            {
                title = gp.Title,
                content = enhancedContent,
                status = "publish",
                categories = new[] { catId },
                tags = tagIds
            };

            var postReq = new HttpRequestMessage(HttpMethod.Post, "/wp-json/wp/v2/posts")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(postObj),
                    Encoding.UTF8,
                    "application/json")
            };
            postReq.Headers.Accept.Clear();
            postReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            var resp = await _http.SendAsync(postReq);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"WP create post error {resp.StatusCode}: {body}");
        }

        //public async Task CreatePostAsync(GeneratedPost gp)
        //{
        //    // 1) Asegurar categoría y tags
        //    var catId = await EnsureCategoryAsync(gp.Category);
        //    var tagIds = await EnsureTagsAsync(gp.Tags);

        //    // 2) Preparar objeto post
        //    var postObj = new
        //    {
        //        title = gp.Title,
        //        content = gp.Content,
        //        status = "publish",
        //        categories = new[] { catId },
        //        tags = tagIds
        //    };

        //    // 3) Crear HttpRequestMessage
        //    var postReq = new HttpRequestMessage(HttpMethod.Post, "/wp-json/wp/v2/posts")
        //    {
        //        Content = new StringContent(
        //            JsonSerializer.Serialize(postObj),
        //            Encoding.UTF8,
        //            "application/json"
        //        )
        //    };
        //    // Forzamos a aceptar cualquier tipo para evitar 406
        //    postReq.Headers.Accept.Clear();
        //    postReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        //    // 4) Enviar y manejar respuesta
        //    var resp = await _http.SendAsync(postReq);
        //    var body = await resp.Content.ReadAsStringAsync();

        //    if (!resp.IsSuccessStatusCode)
        //        throw new ApplicationException($"WP create post error {resp.StatusCode}: {body}");

        //    // (Opcional) Si quieres el ID del nuevo post:
        //    // using var doc = JsonDocument.Parse(body);
        //    // int newPostId = doc.RootElement.GetProperty("id").GetInt32();
        //}

        /// <summary>
        /// Genera un PDF con contenido + banner de texto y lo sube como media. Devuelve la URL pública.
        /// </summary>

        private class NonClosingStream : Stream
        {
            private readonly Stream _inner;
            public NonClosingStream(Stream inner) => _inner = inner;

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count)
                                                      => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin)
                                                      => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count)
                                                      => _inner.Write(buffer, offset, count);

            // Aquí se ignora totalmente el Close/Dispose
            public override void Close() { /* no-op */ }
            protected override void Dispose(bool disposing)
            {
                // no llamamos a _inner.Dispose() para dejar el stream abierto
            }
        }

        public async Task<string> GenerateAndUploadPdfAsync(string htmlContent, string filename)
        {
            // 1) Creamos un MemoryStream real y lo envolvemos
            using var realMs = new MemoryStream();
            var ms = new NonClosingStream(realMs);

            // 2) Creamos el PDF normalmente
            var writer = new PdfWriter(ms);
            var pdfDoc = new PdfDocument(writer);
            var doc = new Document(pdfDoc);

            // 3) Fuente bold
            PdfFont boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

            // 4) Añadimos contenido
            doc.Add(new Paragraph("Contenido del Post")
                .SetFont(boldFont)
                .SetFontSize(16));
            doc.Add(new Paragraph(htmlContent));
            doc.Add(new Paragraph("📢 Patrocinado por AcmeAds: visita acmeads.com"));

            // 5) Cerramos el documento (no cierra realMs)
            doc.Close();

            // 6) Reposicionamos el stream al inicio
            realMs.Position = 0;

            // 7) Creamos el contenido multipart
            var multipart = new MultipartFormDataContent();
            var pdfContent = new StreamContent(realMs);
            pdfContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            multipart.Add(pdfContent, "file", $"{filename}.pdf");

            // 8) Enviamos la petición directamente con PostAsync
            var resp = await _http.PostAsync("/wp-json/wp/v2/media", multipart);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"Error subiendo PDF: {resp.StatusCode} {body}");

            // 9) Parseamos la URL devuelta
            using var docJson = JsonDocument.Parse(body);
            return docJson.RootElement.GetProperty("source_url").GetString()!;
        }


        record WpTerm(int Id, string Name);
    }

}
