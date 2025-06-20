using PublishBlogWordpress;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
       

        services.Configure<OpenAISettings>(ctx.Configuration.GetSection("OpenAI"));
        services.Configure<WordPressSettings>(ctx.Configuration.GetSection("WordPress"));

        services.AddHttpClient(); // para llamar API de WP y OpenAI
        services.AddSingleton<ChatGptService>();
        services.AddSingleton<WordPressService>();
        services.AddHostedService<TrendingPostWorker>();
    })
    .Build();

await host.RunAsync();
