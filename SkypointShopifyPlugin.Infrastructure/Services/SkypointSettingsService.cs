using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Configuration;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class SkypointSettingsService : ISkypointSettingsService
    {
        private readonly HttpClient _httpClient;
        private readonly IShopTokenStore _shopTokenStore;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<SkypointSettingsService> _logger;

        public SkypointSettingsService(
            HttpClient httpClient,
            IShopTokenStore shopTokenStore,
            IMemoryCache memoryCache,
            ILogger<SkypointSettingsService> logger)
        {
            _httpClient = httpClient;
            _shopTokenStore = shopTokenStore;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<SkypointShopifySettings> GetSettingsAsync(string shopDomain)
        {
            shopDomain = NormalizeShop(shopDomain);
            var cacheKey = $"skypoint_settings_{shopDomain}";

            if (_memoryCache.TryGetValue(cacheKey, out SkypointShopifySettings? cachedSettings) && cachedSettings != null)
            {
                return cachedSettings;
            }

            var settings = await FetchSettingsFromShopifyAsync(shopDomain);
            
            // Cache settings for 5 minutes
            _memoryCache.Set(cacheKey, settings, TimeSpan.FromMinutes(5));
            return settings;
        }

        private async Task<SkypointShopifySettings> FetchSettingsFromShopifyAsync(string shopDomain)
        {
            var settings = new SkypointShopifySettings();
            var accessToken = _shopTokenStore.GetToken(shopDomain);

            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("No Shopify access token found for shop {Shop} when loading settings.", shopDomain);
                return settings;
            }

            try
            {
                var graphqlUrl = $"https://{shopDomain}/admin/api/2024-01/graphql.json";
                var query = new
                {
                    query = @"
                        query GetShopMetafields {
                            shop {
                                metafields(first: 50, namespace: ""skypoint_shipping"") {
                                    edges {
                                        node {
                                            key
                                            value
                                        }
                                    }
                                }
                            }
                        }"
                };

                var req = new HttpRequestMessage(HttpMethod.Post, graphqlUrl);
                req.Headers.Add("X-Shopify-Access-Token", accessToken);
                req.Content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");

                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to query shop metafields from Shopify: HTTP {Status}", resp.StatusCode);
                    return settings;
                }

                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);
                var edges = json?["data"]?["shop"]?["metafields"]?["edges"]?.AsArray();

                if (edges == null)
                {
                    return settings;
                }

                var metafields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var edge in edges)
                {
                    var node = edge?["node"];
                    var key = node?["key"]?.ToString();
                    var val = node?["value"]?.ToString();
                    if (!string.IsNullOrEmpty(key) && val != null)
                    {
                        metafields[key] = val;
                    }
                }

                _logger.LogInformation("Shopify metafields loaded for {Shop}: {Metafields}", shopDomain, string.Join(", ", metafields.Select(kv => $"{kv.Key}={kv.Value}")));

                // Map credentials
                if (metafields.TryGetValue("username", out var username)) settings.Username = username;
                if (metafields.TryGetValue("password", out var password)) settings.Password = password;
                if (metafields.TryGetValue("isuat", out var isuatStr)) settings.IsUat = ParseBool(isuatStr, true);

                // Map shipper info
                if (metafields.TryGetValue("shipper_company", out var shipperCompany)) settings.ShipperCompanyName = shipperCompany;
                if (metafields.TryGetValue("shipper_first_name", out var shipperFirst)) settings.ShipperFirstName = shipperFirst;
                if (metafields.TryGetValue("shipper_last_name", out var shipperLast)) settings.ShipperLastName = shipperLast;
                if (metafields.TryGetValue("shipper_email", out var shipperEmail)) settings.ShipperEmail = shipperEmail;
                if (metafields.TryGetValue("shipper_phone", out var shipperPhone)) settings.ShipperPhone = shipperPhone;
                if (metafields.TryGetValue("shipper_address1", out var shipperAddress)) settings.ShipperAddress1 = shipperAddress;
                if (metafields.TryGetValue("shipper_suburb", out var shipperSuburb)) settings.ShipperSuburb = shipperSuburb;
                if (metafields.TryGetValue("shipper_postcode", out var shipperPostcode)) settings.ShipperPostcode = shipperPostcode;
                if (metafields.TryGetValue("shipper_city", out var shipperCity)) settings.ShipperCity = shipperCity;
                if (metafields.TryGetValue("shipper_province", out var shipperProvince)) settings.ShipperProvince = shipperProvince;

                // Map defaults and rules
                if (metafields.TryGetValue("fallback_cost", out var fallbackStr)) settings.FallbackCost = ParseDecimal(fallbackStr, 0);
                if (metafields.TryGetValue("freeship_threshold", out var freeShipStr)) settings.FreeshipThreshold = ParseDecimal(freeShipStr, 0);
                if (metafields.TryGetValue("default_mass", out var massStr)) settings.DefaultMass = ParseDouble(massStr, 15.0);
                if (metafields.TryGetValue("default_length", out var lengthStr)) settings.DefaultLength = ParseDouble(lengthStr, 15.0);
                if (metafields.TryGetValue("default_breadth", out var breadthStr)) settings.DefaultBreadth = ParseDouble(breadthStr, 15.0);
                if (metafields.TryGetValue("default_height", out var heightStr)) settings.DefaultHeight = ParseDouble(heightStr, 10.0);

                // Map triggers/enables
                if (metafields.TryGetValue("enable_road", out var enableRoadStr)) settings.EnableRoad = ParseBool(enableRoadStr, true);
                if (metafields.TryGetValue("enable_air", out var enableAirStr)) settings.EnableAir = ParseBool(enableAirStr, true);
                if (metafields.TryGetValue("enable_counter", out var enableCounterStr)) settings.EnableCounter = ParseBool(enableCounterStr, true);

                // Map renaming
                if (metafields.TryGetValue("rename_road", out var renameRoad)) settings.RenameRoad = renameRoad;
                if (metafields.TryGetValue("rename_air", out var renameAir)) settings.RenameAir = renameAir;
                if (metafields.TryGetValue("rename_counter", out var renameCounter)) settings.RenameCounter = renameCounter;

                // Map sorting
                if (metafields.TryGetValue("sort_road", out var sortRoadStr)) settings.SortRoad = ParseInt(sortRoadStr, 1);
                if (metafields.TryGetValue("sort_air", out var sortAirStr)) settings.SortAir = ParseInt(sortAirStr, 2);
                if (metafields.TryGetValue("sort_counter", out var sortCounterStr)) settings.SortCounter = ParseInt(sortCounterStr, 3);

                // Map markups
                if (metafields.TryGetValue("markup_type", out var markupType)) settings.MarkupType = markupType;
                if (metafields.TryGetValue("markup_road", out var markupRoadStr)) settings.MarkupRoad = ParseDecimal(markupRoadStr, 0);
                if (metafields.TryGetValue("markup_air", out var markupAirStr)) settings.MarkupAir = ParseDecimal(markupAirStr, 0);
                if (metafields.TryGetValue("markup_counter", out var markupCounterStr)) settings.MarkupCounter = ParseDecimal(markupCounterStr, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Shopify settings metafields for {Shop}", shopDomain);
            }

            return settings;
        }

        private static string NormalizeShop(string shop)
            => shop.Trim().ToLowerInvariant()
                   .Replace("https://", "").Replace("http://", "").TrimEnd('/');

        private static bool ParseBool(string val, bool def)
            => bool.TryParse(val, out var res) ? res : def;

        private static decimal ParseDecimal(string val, decimal def)
            => decimal.TryParse(val, out var res) ? res : def;

        private static double ParseDouble(string val, double def)
            => double.TryParse(val, out var res) ? res : def;

        private static int ParseInt(string val, int def)
            => int.TryParse(val, out var res) ? res : def;
    }
}
