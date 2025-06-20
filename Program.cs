using PublishBlogWordpress;
using PdfTutorialsFree.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
       

        services.Configure<OpenAISettings>(ctx.Configuration.GetSection("OpenAI"));
        services.Configure<WordPressSettings>(ctx.Configuration.GetSection("WordPress"));

        services.AddWordPressMedia(
            openAI => openAI.ApiKey = ctx.Configuration["OpenAI:ApiKey"] ?? string.Empty,
            wordpress =>
            {
                wordpress.BaseUrl = ctx.Configuration["WordPress:SiteUrl"] ?? string.Empty;
                wordpress.Username = ctx.Configuration["WordPress:Username"] ?? string.Empty;
                wordpress.AppPassword = ctx.Configuration["WordPress:AppPassword"] ?? string.Empty;
            });

        services.AddSingleton<ChatGptService>();
        services.AddSingleton<WordPressService>();
        services.AddHostedService<TrendingPostWorker>();
    })
    .Build();

await host.RunAsync();
