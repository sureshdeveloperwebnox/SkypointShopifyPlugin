using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using SkypointShopifyPlugin.Core.Common;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    /// <summary>
    /// Production-ready Skypoint API Client integrated with Polly resilience pipelines
    /// for automatic transient fault handling, timeouts, and retries.
    /// </summary>
    public class SkypointApiClient : ISkypointApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly SkypointApiSettings _settings;
        private readonly ILogger<SkypointApiClient> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ResiliencePipeline _resiliencePipeline;

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

            // Configure Polly v8 Resilience Pipeline (Retry + Timeout)
            var retryOptions = new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>(),
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                OnRetry = args =>
                {
                    _logger.LogWarning(LogEventIds.PollyRetryAttempt, "Skypoint API transient failure. Retrying attempt {Attempt} after delay {Delay}s. Exception: {Message}",
                        args.AttemptNumber + 1, args.RetryDelay.TotalSeconds, args.Outcome.Exception?.Message);
                    return default;
                }
            };

            var timeoutOptions = new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            _resiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(retryOptions)
                .AddTimeout(timeoutOptions)
                .Build();
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            var url = _settings.GetLoginUrl();
            _logger.LogInformation("Login request to {Url}", url);

            var content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<LoginResponse>(responseBody, _jsonOptions);
                
                _logger.LogInformation("Login successful for user: {Username}", request.Username);
                return result!;
            });
        }

        public async Task<LoginResponse> RegisterAsync(RegisterRequest request)
        {
            var url = _settings.GetRegisterUrl();
            _logger.LogInformation("Registration request to {Url}", url);

            var content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json"
            );

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<LoginResponse>(responseBody, _jsonOptions);
                
                _logger.LogInformation("Registration successful for email: {Email}", request.Email);
                return result!;
            });
        }

        public async Task<List<RateResponse>> GetRatesAsync(RateRequest request, string authToken)
        {
            var url = _settings.GetRateQuoteUrl();
            var payload = JsonSerializer.Serialize(request, _jsonOptions);
            _logger.LogInformation("Rate request to {Url} | payload: {Payload}", url, payload);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
                httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Rate API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    response.EnsureSuccessStatusCode(); // rethrow as HttpRequestException
                }

                var result = JsonSerializer.Deserialize<List<RateResponse>>(responseBody, _jsonOptions);
                _logger.LogInformation("Retrieved {Count} rate options", result?.Count ?? 0);
                return result ?? new List<RateResponse>();
            });
        }

        public async Task<BookingResponse> CreateBookingAsync(BookingRequest request, string authToken)
        {
            var url = _settings.GetBookingUrl();
            var payload = JsonSerializer.Serialize(request, _jsonOptions);
            _logger.LogInformation("Booking request to {Url} | payload: {Payload}", url, payload);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
                httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Booking API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = JsonSerializer.Deserialize<BookingResponse>(responseBody, _jsonOptions);
                _logger.LogInformation("Booking created with tracking number: {TrackNo}", result?.TrackNo);
                return result!;
            });
        }
    }
}
