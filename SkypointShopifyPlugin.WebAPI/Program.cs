using MediatR;
using Microsoft.AspNetCore.HttpOverrides;
using SkypointShopifyPlugin.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Standard ASP.NET Core config chain:
//   appsettings.json → appsettings.{env}.json → Environment variables → Command-line
// Environment variables override appsettings.json, e.g. set Shopify__ClientId in production.
builder.Configuration.AddEnvironmentVariables();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Add Infrastructure layer with centralized configuration
builder.Services.AddInfrastructure(builder.Configuration);


// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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

// Fallback: any unmatched route serves index.html (SPA-style)
app.MapFallbackToFile("index.html");

app.Run();
