namespace SkypointShopifyPlugin.Core.Common
{
    /// <summary>
    /// Centralized logging Event IDs to enable structured log aggregation,
    /// querying, and alerting in production monitoring tools (e.g., Seq, Elastic, Application Insights).
    /// </summary>
    public static class LogEventIds
    {
        // Security and HMAC Validation
        public const int HmacValidationSuccess = 1001;
        public const int HmacValidationFailure = 1002;
        public const int TokenValidationBypassed = 1003;

        // Background Webhook Processing
        public const int WebhookReceived = 2001;
        public const int WebhookProcessed = 2002;
        public const int WebhookProcessingError = 2003;

        // Polly Resilience and HTTP Clients
        public const int PollyRetryAttempt = 3001;
        public const int PollyCircuitBroken = 3002;
        public const int PollyRequestTimeout = 3003;

        // Data Encryption & Db Migrations
        public const int KeyDerivationSuccess = 4001;
        public const int DecryptionFailure = 4002;
        public const int StartupMigrationSuccess = 4003;
    }
}
