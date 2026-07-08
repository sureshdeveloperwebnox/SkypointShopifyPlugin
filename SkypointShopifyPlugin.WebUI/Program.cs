using SkypointShopifyPlugin.WebUI.Components;
using Microsoft.AspNetCore.HttpOverrides;

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

// Register the scoped HttpClient directly, bypassing IHttpClientFactory to preserve circuit scope for ProtectedSessionStorage
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<SkypointShopifyPlugin.WebUI.Handlers.TokenAuthorizationHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:5126")
    };
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

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

// Proxy /js/* to WebAPI (serves skypoint-pudo.js and other static assets)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/js"))
    {
        var backendUrl = builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:5126";
        var targetUri = new Uri($"{backendUrl.TrimEnd('/')}{context.Request.Path}{context.Request.QueryString}");

        using var jsClient = new HttpClient();
        try
        {
            var jsResponse = await jsClient.GetAsync(targetUri);
            context.Response.StatusCode = (int)jsResponse.StatusCode;
            foreach (var header in jsResponse.Content.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding");
            await jsResponse.Content.CopyToAsync(context.Response.Body);
            return;
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync($"Error proxying JS file: {ex.Message}");
            return;
        }
    }
    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var backendUrl = builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:5126";
        var targetUri = new Uri($"{backendUrl.TrimEnd('/')}{context.Request.Path}{context.Request.QueryString}");

        using var client = new HttpClient();
        
        var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);
        
        // Copy request body
        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            var streamContent = new StreamContent(context.Request.Body);
            requestMessage.Content = streamContent;
            if (context.Request.ContentType != null)
            {
                streamContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }
        }

        // Copy request headers
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                if (requestMessage.Content != null)
                {
                    requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
            else
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        try
        {
            var responseMessage = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
            context.Response.StatusCode = (int)responseMessage.StatusCode;

            // Copy response headers
            foreach (var header in responseMessage.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in responseMessage.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            
            // Remove transfer-encoding chunked to prevent conflicts
            context.Response.Headers.Remove("transfer-encoding");

            await responseMessage.Content.CopyToAsync(context.Response.Body);
            return;
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync($"Error proxying request to backend API: {ex.Message}");
            return;
        }
    }

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
