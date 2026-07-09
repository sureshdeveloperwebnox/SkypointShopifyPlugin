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
                    .Handle<HttpRequestException>(ex => ex.StatusCode == null || (int)ex.StatusCode >= 500)
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

                var result = DeserializeBookingResponse(responseBody);
                _logger.LogInformation("Booking created with tracking number: {TrackNo}", result?.TrackNo);
                return result!;
            });
        }

        public async Task<TrackingResponse> TrackBookingAsync(string trackNo, string authToken)
        {
            if (string.IsNullOrEmpty(trackNo) || (trackNo.Length >= 13 && long.TryParse(trackNo, out _)))
            {
                _logger.LogError("Invalid tracking number '{TrackNo}' passed to TrackBookingAsync. Shopify order ID must not be sent.", trackNo);
                throw new ArgumentException($"Invalid tracking number '{trackNo}'. Shopify order ID must not be sent.", nameof(trackNo));
            }

            var url = _settings.GetTrackingUrl(trackNo);
            _logger.LogInformation("Tracking request to {Url}", url);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Tracking API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint Tracking API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = JsonSerializer.Deserialize<TrackingResponse>(responseBody, _jsonOptions);
                _logger.LogInformation("Retrieved tracking information for {TrackNo}. Events count: {Count}", trackNo, result?.TrackingInfo?.Count ?? 0);
                return result ?? new TrackingResponse();
            });
        }

        public async Task<PudoPointResponse> GetSelectedPudoPointAsync(string guid, string authToken)
        {
            var url = _settings.GetPudoSelectedUrl(guid);
            _logger.LogInformation("Selected PUDO point request to {Url}", url);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("Selected PUDO point for GUID {Guid} not selected yet (404).", guid);
                    return null!;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Selected PUDO Point API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint Selected PUDO API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = JsonSerializer.Deserialize<PudoPointResponse>(responseBody, _jsonOptions);
                _logger.LogInformation("Retrieved selected PUDO point for {Guid}: {Code} - {Name}", guid, result?.Code, result?.Name);
                return result!;
            });
        }

        public async Task<WaybillDownloadResponse> DownloadWaybillAsync(string waybillNumber, string authToken)
        {
            if (string.IsNullOrEmpty(waybillNumber) || (waybillNumber.Length >= 13 && long.TryParse(waybillNumber, out _)))
            {
                _logger.LogError("Invalid waybill number '{WaybillNumber}' passed to DownloadWaybillAsync. Shopify order ID must not be sent.", waybillNumber);
                throw new ArgumentException($"Invalid waybill number '{waybillNumber}'. Shopify order ID must not be sent.", nameof(waybillNumber));
            }

            var url = _settings.GetWaybillDownloadUrl(waybillNumber);
            _logger.LogInformation("Waybill download request to {Url}", url);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Waybill Download API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint Waybill Download API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = JsonSerializer.Deserialize<WaybillDownloadResponse>(responseBody, _jsonOptions);
                _logger.LogInformation("Successfully retrieved waybill download info for {WaybillNumber}. File size: {Length} characters", 
                    waybillNumber, result?.FileStream?.Length ?? 0);
                return result!;
            });
        }

        public async Task<WaybillDownloadResponse> BulkLabelPrintAsync(List<string> bookingIds, string authToken)
        {
            var url = _settings.GetBulkLabelPrintUrl();
            _logger.LogInformation("Bulk label print request to {Url} for {Count} bookings", url, bookingIds.Count);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(bookingIds, _jsonOptions),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Bulk label print API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint bulk label API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = JsonSerializer.Deserialize<WaybillDownloadResponse>(responseBody, _jsonOptions);
                _logger.LogInformation("Bulk label print succeeded. File: {FileName}", result?.FileName);
                return result!;
            });
        }

        public async Task<BookingResponse> GetBookingDetailsAsync(string bookingId, string authToken)
        {
            if (string.IsNullOrEmpty(bookingId) || !Guid.TryParse(bookingId, out _))
            {
                _logger.LogError("Invalid booking ID '{BookingId}' passed to GetBookingDetailsAsync. Booking ID must be a valid GUID.", bookingId);
                throw new ArgumentException($"Invalid booking ID '{bookingId}'. Must be a valid GUID.", nameof(bookingId));
            }

            var url = _settings.GetBookingDetailsUrl(bookingId);
            _logger.LogInformation("Booking details fetch request to {Url}", url);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Booking details fetch API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint booking details fetch API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = DeserializeBookingResponse(responseBody);
                _logger.LogInformation("Successfully retrieved booking details for ID {BookingId}. Status: {Status}", bookingId, result?.Status);
                return result!;
            });
        }

        public async Task<BookingResponse> ProcessBookingAsync(string trackNo, string authToken)
        {
            if (string.IsNullOrEmpty(trackNo) || (trackNo.Length >= 13 && long.TryParse(trackNo, out _)))
            {
                _logger.LogError("Invalid tracking number '{TrackNo}' passed to ProcessBookingAsync. Shopify order ID must not be sent.", trackNo);
                throw new ArgumentException($"Invalid tracking number '{trackNo}'. Shopify order ID must not be sent.", nameof(trackNo));
            }

            var url = _settings.GetProcessBookingUrl(trackNo);
            _logger.LogInformation("Booking process request to {Url}", url);

            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
                httpRequest.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Booking process API returned {Status}: {Body}", (int)response.StatusCode, responseBody);
                    throw new HttpRequestException($"Skypoint booking process API returned {response.StatusCode}: {responseBody}", null, response.StatusCode);
                }

                var result = DeserializeBookingResponse(responseBody);
                _logger.LogInformation("Booking processed successfully for {TrackNo}. Status: {Status}", trackNo, result?.Status);
                return result!;
            });
        }

        private BookingResponse DeserializeBookingResponse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new BookingResponse();

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("details", out var detailsElement) && detailsElement.ValueKind != JsonValueKind.Null)
                {
                    return JsonSerializer.Deserialize<BookingResponse>(detailsElement.GetRawText(), _jsonOptions) ?? new BookingResponse();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse json or extract 'details' property, falling back to direct deserialization");
            }

            return JsonSerializer.Deserialize<BookingResponse>(json, _jsonOptions) ?? new BookingResponse();
        }
    }
}
