using MediatR;
using Microsoft.AspNetCore.HttpOverrides;
using SkypointShopifyPlugin.Infrastructure.DependencyInjection;
using SkypointShopifyPlugin.Infrastructure.Services;
var builder = WebApplication.CreateBuilder(args);

// Standard ASP.NET Core config chain:
//   appsettings.json → appsettings.{env}.json → Environment variables → Command-line
// Environment variables override appsettings.json, e.g. set Shopify__ClientId in production.
builder.Configuration.AddInMemoryCollection(LoadEnvFiles(
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(builder.Environment.ContentRootPath, ".env"),
    Path.Combine(builder.Environment.ContentRootPath, "..", ".env")));
builder.Configuration.AddEnvironmentVariables();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Add Infrastructure layer with centralized configuration
builder.Services.AddInfrastructure(builder.Configuration);

// Both token stores share ContentRootPath/data and the same encryption key
var tokenDataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
builder.Services.Configure<ShopTokenStoreOptions>(opts => opts.DataDirectory = tokenDataDir);
builder.Services.Configure<SkypointCredentialStoreOptions>(opts => opts.DataDirectory = tokenDataDir);
builder.Services.Configure<SkypointOrderStoreOptions>(opts => opts.DataDirectory = Path.Combine(tokenDataDir, "orders"));


// Add CORS — allow all origins with credentials for ngrok/iframe compatibility
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

app.UseCors("AllowAll");
// app.UseHttpsRedirection(); // Disabled for ngrok tunnel compatibility
app.UseDefaultFiles(); // Serves index.html for root "/"
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();

// Unknown API routes should stay API-shaped instead of falling through to the SPA file fallback.
app.Map("/api/{**catchAll}", () => Results.NotFound());

// Fallback: any unmatched route serves index.html (SPA-style)
app.MapFallbackToFile("index.html");

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
