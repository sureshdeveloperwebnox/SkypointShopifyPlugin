using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class ShopifyAdminService : IShopifyAdminService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ShopifyAdminService> _logger;

        public ShopifyAdminService(HttpClient httpClient, ILogger<ShopifyAdminService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<bool> RegisterCarrierServiceAsync(string shopDomain, string accessToken, string carrierServiceUrl)
        {
            var (success, _) = await RegisterAndAssignCarrierServiceAsync(shopDomain, accessToken, carrierServiceUrl);
            return success;
        }

        public async Task<(bool success, string message)> SyncWebhooksAsync(
            string shopDomain,
            string accessToken,
            string publicBaseUrl)
        {
            try
            {
                publicBaseUrl = publicBaseUrl.TrimEnd('/');
                var desiredWebhooks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["orders/create"] = $"{publicBaseUrl}/api/shopify/orders/create",
                    ["orders/updated"] = $"{publicBaseUrl}/api/shopify/orders/updated",
                    ["orders/cancelled"] = $"{publicBaseUrl}/api/shopify/orders/cancelled",
                    ["app/uninstalled"] = $"{publicBaseUrl}/api/shopify/app/uninstalled"
                };

                var managedTopics = desiredWebhooks.Keys
                    .Append("orders/paid")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var existing = await GetWebhooksAsync(shopDomain, accessToken);
                var deleted = 0;
                var created = 0;

                foreach (var webhook in existing)
                {
                    if (!managedTopics.Contains(webhook.Topic))
                        continue;

                    if (desiredWebhooks.TryGetValue(webhook.Topic, out var desiredAddress)
                        && webhook.Address.Equals(desiredAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (await DeleteWebhookAsync(shopDomain, accessToken, webhook.Id))
                        deleted++;
                }

                existing = await GetWebhooksAsync(shopDomain, accessToken);
                foreach (var (topic, address) in desiredWebhooks)
                {
                    var alreadyExists = existing.Any(webhook =>
                        webhook.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase)
                        && webhook.Address.Equals(address, StringComparison.OrdinalIgnoreCase));

                    if (!alreadyExists && await CreateWebhookAsync(shopDomain, accessToken, topic, address))
                        created++;
                }

                return (true, $"Webhook sync complete. Deleted stale: {deleted}, created: {created}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Shopify webhooks for {Shop}", shopDomain);
                return (false, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the carrier service via REST API.
        /// Shopify automatically makes it available in the shipping profile UI
        /// once the carrier service exists — no GraphQL profile mutation needed.
        /// </summary>
        public async Task<(bool success, string message)> RegisterAndAssignCarrierServiceAsync(
            string shopDomain, string accessToken, string carrierServiceUrl)
        {
            try
            {
                _logger.LogInformation("Starting carrier registration for {Shop}, callback: {Url}", shopDomain, carrierServiceUrl);

                // ── 1. Check if a Skypoint Shipping carrier already exists ────────────────
                var existing = await GetExistingCarrierAsync(shopDomain, accessToken);
                if (existing != null)
                {
                    var (existingId, existingCallbackUrl) = existing.Value;

                    // If the callback URL is already correct — make sure it's assigned to all zones
                    if (existingCallbackUrl.Equals(carrierServiceUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Carrier (id={Id}) callback URL already up-to-date. Checking zone assignments...", existingId);
                        var assigned = await AssignCarrierToAllZonesAsync(shopDomain, accessToken, existingId);
                        var zoneMsg = assigned > 0
                            ? $" Added to {assigned} shipping zone(s) automatically."
                            : " Checked zone assignments.";
                        return (true, $"Skypoint Shipping already registered (id={existingId}).{zoneMsg}");
                    }

                    // Try to update the callback URL
                    _logger.LogInformation("Carrier (id={Id}) exists, updating callback URL from {Old} → {New}",
                        existingId, existingCallbackUrl, carrierServiceUrl);
                    var updated = await UpdateCarrierCallbackAsync(shopDomain, accessToken, existingId, carrierServiceUrl);
                    if (updated)
                    {
                        var assigned = await AssignCarrierToAllZonesAsync(shopDomain, accessToken, existingId);
                        var zoneMsg = assigned > 0
                            ? $" Added to {assigned} shipping zone(s) automatically."
                            : " Checked zone assignments.";
                        return (true, $"Skypoint Shipping callback URL updated (id={existingId}).{zoneMsg}");
                    }

                    // 403 — current token doesn't own this carrier (created by a different app/token).
                    // Delete it and recreate under the current token so ownership is correct.
                    _logger.LogWarning("Cannot update carrier (id={Id}) — ownership mismatch. Deleting and recreating.", existingId);
                    var deleted = await DeleteCarrierAsync(shopDomain, accessToken, existingId);
                    if (!deleted)
                    {
                        // Can't delete either — just report success; it still works even with old URL
                        // (ngrok fixed URL or load-balanced env). A fresh reinstall will fix ownership.
                        _logger.LogWarning("Could not delete carrier (id={Id}). Carrier is active but URL may be stale.", existingId);
                        return (true, $"Skypoint Shipping active (id={existingId}). Reinstall the app to update the callback URL.");
                    }

                    _logger.LogInformation("Carrier (id={Id}) deleted — recreating under current token", existingId);
                }

                // ── 2. Create carrier service ──────────────────────────────────────────────
                var url = $"https://{shopDomain}/admin/api/2024-01/carrier_services.json";
                var bodyObj = new
                {
                    carrier_service = new
                    {
                        name = "Skypoint Shipping",
                        callback_url = carrierServiceUrl,
                        service_discovery = true
                    }
                };

                var response = await PostJsonAsync(url, accessToken, bodyObj);
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Create carrier response {Status}: {Content}", response.StatusCode, content);

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 422)
                    {
                        if (content.Contains("Carrier Calculated Shipping must be enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            var msg = $"Carrier Calculated Shipping is not enabled for '{shopDomain}'. " +
                                      $"Fix: Shopify Partner Dashboard → Stores → find {shopDomain} → '...' → 'Enable carrier-calculated shipping rates'. Then reinstall.";
                            _logger.LogWarning(msg);
                            return (false, msg);
                        }
                        _logger.LogWarning("422 creating carrier: {Content}", content);
                        return (true, "Carrier service may already exist. Check Shopify Shipping settings.");
                    }
                    return (false, $"Shopify API error {response.StatusCode}: {content}");
                }

                var json = JsonNode.Parse(content);
                var newId = json?["carrier_service"]?["id"]?.ToString();
                _logger.LogInformation("Carrier service created with id={Id}", newId);

                if (!string.IsNullOrEmpty(newId))
                {
                    var assigned = await AssignCarrierToAllZonesAsync(shopDomain, accessToken, newId);
                    var zoneMsg = assigned > 0
                        ? $" Added to {assigned} shipping zone(s) automatically."
                        : " Could not auto-assign to zones — add manually in Shopify Settings → Shipping.";
                    return (true, $"Skypoint Shipping registered (id={newId}).{zoneMsg}");
                }

                return (true, $"Skypoint Shipping registered. Add it to your shipping zones in Shopify Settings → Shipping.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering carrier for {Shop}", shopDomain);
                return (false, $"Exception: {ex.Message}");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────

        private record ShopifyWebhook(string Id, string Topic, string Address);

        private async Task<List<ShopifyWebhook>> GetWebhooksAsync(string shopDomain, string accessToken)
        {
            var url = $"https://{shopDomain}/admin/api/2024-01/webhooks.json";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Shopify-Access-Token", accessToken);

            var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("Existing webhooks response {Status}: {Body}", resp.StatusCode, body);

            if (!resp.IsSuccessStatusCode)
                return new List<ShopifyWebhook>();

            var json = JsonNode.Parse(body);
            var webhooks = json?["webhooks"]?.AsArray();
            if (webhooks == null)
                return new List<ShopifyWebhook>();

            return webhooks
                .Select(webhook => new ShopifyWebhook(
                    webhook?["id"]?.ToString() ?? string.Empty,
                    webhook?["topic"]?.ToString() ?? string.Empty,
                    webhook?["address"]?.ToString() ?? string.Empty))
                .Where(webhook => !string.IsNullOrEmpty(webhook.Id))
                .ToList();
        }

        private async Task<bool> DeleteWebhookAsync(string shopDomain, string accessToken, string webhookId)
        {
            var url = $"https://{shopDomain}/admin/api/2024-01/webhooks/{webhookId}.json";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Add("X-Shopify-Access-Token", accessToken);

            var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("Delete webhook {WebhookId} response {Status}: {Body}", webhookId, resp.StatusCode, body);

            return resp.IsSuccessStatusCode;
        }

        private async Task<bool> CreateWebhookAsync(string shopDomain, string accessToken, string topic, string address)
        {
            var url = $"https://{shopDomain}/admin/api/2024-01/webhooks.json";
            var bodyObj = new
            {
                webhook = new
                {
                    topic,
                    address,
                    format = "json"
                }
            };

            var resp = await PostJsonAsync(url, accessToken, bodyObj);
            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("Create webhook {Topic} response {Status}: {Body}", topic, resp.StatusCode, body);

            return resp.IsSuccessStatusCode;
        }

        private async Task<int> AssignCarrierToAllZonesAsync(string shopDomain, string accessToken, string carrierId)
        {
            try
            {
                var graphqlUrl = $"https://{shopDomain}/admin/api/2024-01/graphql.json";
                var carrierGid = $"gid://shopify/DeliveryCarrierService/{carrierId}";

                // Get all delivery profiles and their zones
                var profilesQuery = new StringContent(
                    "{\"query\":\"{ deliveryProfiles(first:10){ edges{ node{ id profileLocationGroups{ locationGroupZones(first:20){ edges{ node{ zone{ id name } } } } } } } } }\"}",
                    Encoding.UTF8, "application/json");

                var profReq = new HttpRequestMessage(HttpMethod.Post, graphqlUrl);
                profReq.Headers.Add("X-Shopify-Access-Token", accessToken);
                profReq.Content = profilesQuery;
                var profResp = await _httpClient.SendAsync(profReq);
                var profBody = await profResp.Content.ReadAsStringAsync();
                _logger.LogInformation("Delivery profiles: {Body}", profBody);

                var profJson = JsonNode.Parse(profBody);
                var profiles = profJson?["data"]?["deliveryProfiles"]?["edges"]?.AsArray();
                if (profiles == null) return 0;

                var assigned = 0;
                foreach (var profileEdge in profiles)
                {
                    var profileId = profileEdge?["node"]?["id"]?.ToString();
                    if (string.IsNullOrEmpty(profileId)) continue;

                    var locationGroups = profileEdge?["node"]?["profileLocationGroups"]?.AsArray();
                    if (locationGroups == null) continue;

                    foreach (var lg in locationGroups)
                    {
                        var zoneEdges = lg?["locationGroupZones"]?["edges"]?.AsArray();
                        if (zoneEdges == null) continue;

                        foreach (var zoneEdge in zoneEdges)
                        {
                            var zoneId = zoneEdge?["node"]?["zone"]?["id"]?.ToString();
                            if (string.IsNullOrEmpty(zoneId)) continue;

                            var mutation = $"{{\"query\":\"mutation {{ deliveryProfileUpdate(id: \\\"{profileId}\\\", profile: {{ locationGroupsToUpdate: [{{ zonesToUpdate: [{{ id: \\\"{zoneId}\\\", methodDefinitionsToCreate: [{{ name: \\\"Skypoint Shipping\\\", description: \\\"Live rates by Skypoint\\\", active: true, rateProvider: {{ carrierService: {{ id: \\\"{carrierGid}\\\" }} }} }}] }}] }}] }}) {{ profile {{ id }} userErrors {{ field message }} }} }}\"}}";

                            var mutReq = new HttpRequestMessage(HttpMethod.Post, graphqlUrl);
                            mutReq.Headers.Add("X-Shopify-Access-Token", accessToken);
                            mutReq.Content = new StringContent(mutation, Encoding.UTF8, "application/json");
                            var mutResp = await _httpClient.SendAsync(mutReq);
                            var mutBody = await mutResp.Content.ReadAsStringAsync();

                            var mutJson = JsonNode.Parse(mutBody);
                            var userErrors = mutJson?["data"]?["deliveryProfileUpdate"]?["userErrors"]?.AsArray();
                            if (userErrors == null || userErrors.Count == 0)
                            {
                                _logger.LogInformation("Assigned Skypoint Shipping to zone {Zone}", zoneId);
                                assigned++;
                            }
                            else
                            {
                                _logger.LogWarning("Could not assign to zone {Zone}: {Errors}", zoneId, mutBody);
                            }
                        }
                    }
                }
                return assigned;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error assigning carrier to zones for {Shop}", shopDomain);
                return 0;
            }
        }

        /// <summary>Returns (id, callback_url) for an existing "Skypoint Shipping" carrier, or null if not found.</summary>
        private async Task<(string id, string callbackUrl)?> GetExistingCarrierAsync(string shopDomain, string accessToken)
        {
            var url = $"https://{shopDomain}/admin/api/2024-01/carrier_services.json";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Shopify-Access-Token", accessToken);

            var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("Existing carrier services: {Body}", body);

            var json = JsonNode.Parse(body);
            var services = json?["carrier_services"]?.AsArray();
            if (services == null) return null;

            foreach (var svc in services)
            {
                var name = svc?["name"]?.ToString();
                if (name == "Skypoint Shipping")
                {
                    var id = svc?["id"]?.ToString() ?? string.Empty;
                    var callbackUrl = svc?["callback_url"]?.ToString() ?? string.Empty;
                    return (id, callbackUrl);
                }
            }
            return null;
        }

        private async Task<bool> DeleteCarrierAsync(string shopDomain, string accessToken, string carrierId)
        {
            var url = $"https://{shopDomain}/admin/api/2024-01/carrier_services/{carrierId}.json";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            req.Headers.Add("X-Shopify-Access-Token", accessToken);
            var resp = await _httpClient.SendAsync(req);
            _logger.LogInformation("Delete carrier (id={Id}) → {Status}", carrierId, resp.StatusCode);
            return resp.IsSuccessStatusCode;
        }

        private async Task<bool> UpdateCarrierCallbackAsync(string shopDomain, string accessToken, string carrierId, string newCallbackUrl)
        {
            var url = $"https://{shopDomain}/admin/api/2024-01/carrier_services/{carrierId}.json";
            var bodyObj = new
            {
                carrier_service = new
                {
                    id = carrierId,
                    callback_url = newCallbackUrl
                }
            };

            var response = await PutJsonAsync(url, accessToken, bodyObj);
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Update carrier callback {Status}: {Content}", response.StatusCode, content);
            return response.IsSuccessStatusCode;
        }

        private async Task<HttpResponseMessage> PostJsonAsync(string url, string accessToken, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Shopify-Access-Token", accessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(req);
        }

        private async Task<HttpResponseMessage> PutJsonAsync(string url, string accessToken, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Add("X-Shopify-Access-Token", accessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(req);
        }

        public async Task<string> GetOrdersJsonAsync(string shopDomain, string accessToken, DateTime? since = null)
        {
            try
            {
                var url = $"https://{shopDomain}/admin/api/2024-01/orders.json";
                
                if (since.HasValue)
                {
                    var sinceParam = since.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    url += $"?created_at_min={Uri.EscapeDataString(sinceParam)}&status=any";
                }

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("X-Shopify-Access-Token", accessToken);

                var resp = await _httpClient.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to get orders from Shopify: {Status} - {Body}", resp.StatusCode, body);
                    return "[]";
                }

                return body;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching orders from Shopify for {Shop}", shopDomain);
                return "[]";
            }
        }
    }
}
