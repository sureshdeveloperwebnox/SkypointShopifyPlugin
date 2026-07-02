using SkypointShopifyPlugin.WebUI.Components;

var builder = WebApplication.CreateBuilder(args);

// Load .env configuration from root or parent directory
builder.Configuration.AddInMemoryCollection(LoadEnvFiles(
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(builder.Environment.ContentRootPath, ".env"),
    Path.Combine(builder.Environment.ContentRootPath, "..", ".env")));
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Suppress default X-Frame-Options SAMEORIGIN header added by Antiforgery
builder.Services.AddAntiforgery(options =>
{
    options.SuppressXFrameOptionsHeader = true;
});

// Register the authorization handler
builder.Services.AddTransient<SkypointShopifyPlugin.WebUI.Handlers.TokenAuthorizationHandler>();

// Register HttpClient to communicate with WebAPI with automatic JWT injection
builder.Services.AddHttpClient("BackendApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:5126");
})
.AddHttpMessageHandler<SkypointShopifyPlugin.WebUI.Handlers.TokenAuthorizationHandler>();

// Register the scoped HttpClient resolved from HttpClientFactory so components get the configured instance
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("BackendApi"));

var app = builder.Build();

// Middleware to strip X-Frame-Options and configure Content-Security-Policy to allow Shopify framing
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.Remove("X-Frame-Options");
        // Replace or append frame-ancestors to permit admin.shopify.com and all myshopify stores
        context.Response.Headers.Remove("Content-Security-Policy");
        context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors 'self' https://admin.shopify.com https://*.myshopify.com;");
        return Task.CompletedTask;
    });
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static IEnumerable<KeyValuePair<string, string?>> LoadEnvFiles(params string[] paths)
{
    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!File.Exists(path)) continue;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = line[..separatorIndex].Trim().Replace("__", ":");
            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            values[key] = value;
        }
    }

    return values;
}
