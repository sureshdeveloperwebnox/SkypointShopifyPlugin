using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class SkypointApiClient : ISkypointApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly SkypointApiSettings _settings;
        private readonly ILogger<SkypointApiClient> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public SkypointApiClient(
            HttpClient httpClient,
            IOptions<SkypointApiSettings> settings,
            ILogger<SkypointApiClient> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            var url = $"{_settings.BaseUrl}{_settings.LoginEndpoint}";
            _logger.LogInformation("Login request to {Url}", url);

            var content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LoginResponse>(responseBody, _jsonOptions);
            
            _logger.LogInformation("Login successful for user: {Username}", request.Username);
            return result!;
        }

        public async Task<List<RateResponse>> GetRatesAsync(RateRequest request, string authToken)
        {
            var url = $"{_settings.BaseUrl}{_settings.RateEndpoint}";
            _logger.LogInformation("Rate request to {Url}", url);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<List<RateResponse>>(responseBody, _jsonOptions);
            
            _logger.LogInformation("Retrieved {Count} rate options", result?.Count ?? 0);
            return result ?? new List<RateResponse>();
        }

        public async Task<BookingResponse> CreateBookingAsync(BookingRequest request, string authToken)
        {
            var url = $"{_settings.BaseUrl}{_settings.BookingEndpoint}";
            _logger.LogInformation("Booking request to {Url}", url);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BookingResponse>(responseBody, _jsonOptions);
            
            _logger.LogInformation("Booking created with tracking number: {TrackNo}", result?.TrackNo);
            return result!;
        }
    }
}
