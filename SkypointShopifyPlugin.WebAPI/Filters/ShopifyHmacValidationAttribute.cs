using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Common;

namespace SkypointShopifyPlugin.WebAPI.Filters
{
    /// <summary>
    /// Validates that incoming requests contain a valid X-Shopify-Hmac-Sha256 signature header.
    /// Used to secure Webhook endpoints and Carrier Service rate check endpoints.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class ShopifyHmacValidationAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var logger = httpContext.RequestServices.GetRequiredService<ILogger<ShopifyHmacValidationAttribute>>();
            var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();

            var webhookSecret = configuration["Shopify:WebhookSecret"];
            var clientSecret  = configuration["Shopify:ClientSecret"];

            if (!httpContext.Request.Headers.TryGetValue("X-Shopify-Hmac-Sha256", out var signatureHeader))
            {
                logger.LogWarning(LogEventIds.HmacValidationFailure, "Rejected request: Missing 'X-Shopify-Hmac-Sha256' signature header.");
                context.Result = new UnauthorizedResult();
                return;
            }

            var signature = signatureHeader.ToString();
            if (string.IsNullOrEmpty(signature))
            {
                logger.LogWarning(LogEventIds.HmacValidationFailure, "Rejected request: Empty 'X-Shopify-Hmac-Sha256' signature.");
                context.Result = new UnauthorizedResult();
                return;
            }

            // Enable buffering and read request stream
            httpContext.Request.EnableBuffering();
            httpContext.Request.Body.Position = 0;

            string body;
            using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }

            // Reset position so model binding can parse body after filter execution
            httpContext.Request.Body.Position = 0;

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature);
            }
            catch (Exception ex)
            {
                logger.LogError(LogEventIds.HmacValidationFailure, ex, "Error parsing signature bytes: {Signature}", signature);
                context.Result = new UnauthorizedResult();
                return;
            }

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            bool hasValidSecret = false;

            // 1. Try ClientSecret — Shopify signs carrier service rate requests with the app's Client Secret
            if (!string.IsNullOrEmpty(clientSecret) && clientSecret != "YOUR_CLIENT_SECRET")
            {
                hasValidSecret = true;
                using var hmacClient = new HMACSHA256(Encoding.UTF8.GetBytes(clientSecret));
                var hashClient = hmacClient.ComputeHash(bodyBytes);
                if (CryptographicOperations.FixedTimeEquals(signatureBytes, hashClient))
                {
                    logger.LogInformation(LogEventIds.HmacValidationSuccess, "Shopify HMAC verification succeeded using ClientSecret.");
                    await next();
                    return;
                }
            }

            // 2. Try WebhookSecret — used for standard app webhooks
            if (!string.IsNullOrEmpty(webhookSecret) && webhookSecret != "YOUR_WEBHOOK_SECRET")
            {
                hasValidSecret = true;
                using var hmacWebhook = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
                var hashWebhook = hmacWebhook.ComputeHash(bodyBytes);
                if (CryptographicOperations.FixedTimeEquals(signatureBytes, hashWebhook))
                {
                    logger.LogInformation(LogEventIds.HmacValidationSuccess, "Shopify HMAC verification succeeded using WebhookSecret.");
                    await next();
                    return;
                }
            }

            // If no secrets are configured at all, allow in dev/test mode
            if (!hasValidSecret)
            {
                logger.LogWarning(LogEventIds.TokenValidationBypassed, "No Shopify secrets configured. Skipping validation (Development/Test Mode).");
                await next();
                return;
            }

            logger.LogWarning(LogEventIds.HmacValidationFailure, "Rejected request: Shopify HMAC verification failed against both ClientSecret and WebhookSecret.");
            context.Result = new UnauthorizedResult();
        }
    }
}
