# AutoPublishWordpress

This worker service automatically publishes trending blog posts to a WordPress site. It uses OpenAI's GPT models to generate the content and the iText library to create a downloadable PDF of each post.

## Project goals
- Periodically obtain trending topics from GPT.
- Generate SEO optimized articles for those topics.
- Publish the posts with appropriate categories and tags to WordPress.
- Attach a PDF version of the content using iText and link it in the post.

## Prerequisites
- [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
- An existing WordPress site with an [Application Password](https://wordpress.com/support/application-passwords/) and username.
- A valid OpenAI API key.

## Configuration
Configuration values can be placed in `appsettings.json` or stored via user secrets. Two sections are required:

```json
{
  "OpenAI": {
    "ApiKey": "YOUR_OPENAI_KEY"
  },
  "WordPress": {
    "SiteUrl": "https://your-site.example",
    "Username": "your-user",
    "AppPassword": "application-password"
  }
}
```

Alternatively you can run `dotnet user-secrets set` commands with the same keys.

## Build and run
Restore dependencies and run the worker service with:

```bash
dotnet run --project PublishBlogWordpress.csproj
```

The service will start fetching trending topics and posting to WordPress every few hours.
