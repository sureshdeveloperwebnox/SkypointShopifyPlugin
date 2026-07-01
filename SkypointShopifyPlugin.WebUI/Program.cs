using SkypointShopifyPlugin.WebUI.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Suppress default X-Frame-Options SAMEORIGIN header added by Antiforgery
builder.Services.AddAntiforgery(options =>
{
    options.SuppressXFrameOptionsHeader = true;
});

// Register HttpClient to communicate with WebAPI
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri(builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:5126") 
});

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
