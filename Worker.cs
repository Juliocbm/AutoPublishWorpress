namespace PublishBlogWordpress
{
    public class TrendingPostWorker : BackgroundService
    {
        readonly ChatGptService _chat;
        readonly WordPressService _wp;
        readonly ILogger<TrendingPostWorker> _log;

        public TrendingPostWorker(
            ChatGptService chat,
            WordPressService wp,
            ILogger<TrendingPostWorker> log)
        {
            _chat = chat;
            _wp = wp;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            // Intervalo: cada 6 horas
            var intervalo = TimeSpan.FromHours(6);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _log.LogInformation("Obteniendo temas en tendencia...");
                    var topics = await _chat.GetTrendingTopicsAsync();
                    foreach (var t in topics)
                    {
                        _log.LogInformation("Generando post para tema: {Tema}", t);
                        var gp = await _chat.GeneratePostAsync(t);
                        await _wp.CreatePostAsync(gp);
                        _log.LogInformation("Publicado: {Title}", gp.Title);
                        // Pequena pausa entre posts
                        await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error en ciclo de publicación");
                }

                _log.LogInformation("Esperando {Intervalo} para siguiente corrida", intervalo);
                await Task.Delay(intervalo, ct);
            }
        }
    }

}
