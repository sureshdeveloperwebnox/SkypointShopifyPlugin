using MediatR;
using Microsoft.AspNetCore.HttpOverrides;
using SkypointShopifyPlugin.Infrastructure.DependencyInjection;
using SkypointShopifyPlugin.Infrastructure.Services;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Standard ASP.NET Core config chain:
//   appsettings.json → appsettings.{env}.json → Environment variables → Command-line
// Environment variables override appsettings.json, e.g. set Shopify__ClientId in production.
builder.Configuration.AddInMemoryCollection(LoadEnvFiles(
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(builder.Environment.ContentRootPath, ".env"),
    Path.Combine(builder.Environment.ContentRootPath, "..", ".env")));
builder.Configuration.AddEnvironmentVariables();

// Load saved configuration from app_config.json if it exists
// This overrides appsettings.json and .env values
LoadAppConfig(builder.Configuration);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure JWT Authentication
var encryptionKey = builder.Configuration["EncryptionKey"] ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "SkypointShopifyPluginDefaultSecretKey32BytesForSigningTokens!";
byte[] jwtKeyBytes;
using (var sha256 = SHA256.Create())
{
    jwtKeyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey));
}
var jwtSigningKey = new SymmetricSecurityKey(jwtKeyBytes);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = jwtSigningKey,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

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
app.UseMiddleware<SkypointShopifyPlugin.WebAPI.Middleware.ApiExceptionMiddleware>();

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
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            ctx.Context.Response.Headers.Append("Pragma", "no-cache");
            ctx.Context.Response.Headers.Append("Expires", "0");
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Redirect root "/" to setup page for first-time configuration
app.MapGet("/", () => Results.Redirect("/setup"));

// Unknown API routes should stay API-shaped instead of falling through to the SPA file fallback.
app.Map("/api/{**catchAll}", () => Results.NotFound());

app.Run();

static void LoadAppConfig(IConfiguration configuration)
{
    try
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "app_config.json");
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("No app_config.json found. Using default configuration.");
            return;
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<JsonElement>(json);

        if (config.ValueKind == JsonValueKind.Undefined || config.ValueKind == JsonValueKind.Null)
        {
            Console.WriteLine("Failed to parse app_config.json. Using default configuration.");
            return;
        }

        Console.WriteLine("Loading configuration from app_config.json...");

        // Load Shopify configuration
        if (config.TryGetProperty("shopify", out var shopify))
        {
            if (shopify.TryGetProperty("clientId", out var clientId) && clientId.ValueKind != JsonValueKind.Null)
                configuration["Shopify:ClientId"] = clientId.GetString();
            if (shopify.TryGetProperty("clientSecret", out var clientSecret) && clientSecret.ValueKind != JsonValueKind.Null)
                configuration["Shopify:ClientSecret"] = clientSecret.GetString();
            if (shopify.TryGetProperty("redirectUri", out var redirectUri) && redirectUri.ValueKind != JsonValueKind.Null)
                configuration["Shopify:RedirectUri"] = redirectUri.GetString();
            if (shopify.TryGetProperty("webhookSecret", out var webhookSecret) && webhookSecret.ValueKind != JsonValueKind.Null)
                configuration["Shopify:WebhookSecret"] = webhookSecret.GetString();
        }

        // Load Skypoint API configuration
        if (config.TryGetProperty("skypointApi", out var skypointApi))
        {
            if (skypointApi.TryGetProperty("baseUrl", out var baseUrl) && baseUrl.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:BaseUrl"] = baseUrl.GetString();
            if (skypointApi.TryGetProperty("loginEndpoint", out var loginEndpoint) && loginEndpoint.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:LoginEndpoint"] = loginEndpoint.GetString();
            if (skypointApi.TryGetProperty("registerEndpoint", out var registerEndpoint) && registerEndpoint.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:RegisterEndpoint"] = registerEndpoint.GetString();
            if (skypointApi.TryGetProperty("rateEndpoint", out var rateEndpoint) && rateEndpoint.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:RateEndpoint"] = rateEndpoint.GetString();
            if (skypointApi.TryGetProperty("bookingEndpoint", out var bookingEndpoint) && bookingEndpoint.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:BookingEndpoint"] = bookingEndpoint.GetString();
            if (skypointApi.TryGetProperty("timeoutSeconds", out var timeoutSeconds) && timeoutSeconds.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:TimeoutSeconds"] = timeoutSeconds.ToString();
            if (skypointApi.TryGetProperty("maxRetryAttempts", out var maxRetryAttempts) && maxRetryAttempts.ValueKind != JsonValueKind.Null)
                configuration["SkypointApi:MaxRetryAttempts"] = maxRetryAttempts.ToString();
        }

        // Load parcel dimensions
        if (config.TryGetProperty("skypointMappings", out var skypointMappings))
        {
            if (skypointMappings.TryGetProperty("defaultParcelLength", out var parcelLength) && parcelLength.ValueKind != JsonValueKind.Null)
                configuration["SkypointMappings:DefaultParcelLength"] = parcelLength.ToString();
            if (skypointMappings.TryGetProperty("defaultParcelBreadth", out var parcelBreadth) && parcelBreadth.ValueKind != JsonValueKind.Null)
                configuration["SkypointMappings:DefaultParcelBreadth"] = parcelBreadth.ToString();
            if (skypointMappings.TryGetProperty("defaultParcelHeight", out var parcelHeight) && parcelHeight.ValueKind != JsonValueKind.Null)
                configuration["SkypointMappings:DefaultParcelHeight"] = parcelHeight.ToString();
            if (skypointMappings.TryGetProperty("defaultParcelMass", out var parcelMass) && parcelMass.ValueKind != JsonValueKind.Null)
                configuration["SkypointMappings:DefaultParcelMass"] = parcelMass.ToString();
            if (skypointMappings.TryGetProperty("defaultParcelType", out var parcelType) && parcelType.ValueKind != JsonValueKind.Null)
                configuration["SkypointMappings:DefaultParcelType"] = parcelType.GetString();
        }

        Console.WriteLine("Configuration loaded successfully from app_config.json");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to load app_config.json: {ex.Message}. Using default configuration.");
    }
}

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
