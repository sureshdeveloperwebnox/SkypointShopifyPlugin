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
            if (string.IsNullOrEmpty(webhookSecret) || webhookSecret == "YOUR_WEBHOOK_SECRET")
            {
                logger.LogWarning(LogEventIds.TokenValidationBypassed, "Shopify WebhookSecret is not configured. Skipping signature validation (Development/Test Mode).");
                await next();
                return;
            }

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

            // Compute HMAC hash
            var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            using var hmac = new HMACSHA256(secretBytes);
            var hashBytes = hmac.ComputeHash(bodyBytes);

            try
            {
                var signatureBytes = Convert.FromBase64String(signature);
                if (CryptographicOperations.FixedTimeEquals(signatureBytes, hashBytes))
                {
                    logger.LogInformation(LogEventIds.HmacValidationSuccess, "Shopify HMAC verification succeeded.");
                    await next();
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(LogEventIds.HmacValidationFailure, ex, "Error occurred during signature bytes parsing: {Signature}", signature);
            }

            logger.LogWarning(LogEventIds.HmacValidationFailure, "Rejected request: Shopify HMAC verification failed.");
            context.Result = new UnauthorizedResult();
        }
    }
}
