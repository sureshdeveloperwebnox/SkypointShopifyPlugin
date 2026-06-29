using System.Text.Json.Serialization;

namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class LoginResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("createAt")]
        public DateTime CreateAt { get; set; }
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
        [JsonPropertyName("userType")]
        public string UserType { get; set; } = string.Empty;
        [JsonPropertyName("owner")]
        public Owner Owner { get; set; } = new();
        [JsonPropertyName("token")]
        public Token Token { get; set; } = new();
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
        [JsonPropertyName("permissions")]
        public List<string> Permissions { get; set; } = new();
        [JsonPropertyName("accountNo")]
        public string AccountNo { get; set; } = string.Empty;
        [JsonPropertyName("canAddStickers")]
        public string CanAddStickers { get; set; } = string.Empty;
    }

    public class Owner
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        [JsonPropertyName("createAt")]
        public DateTime CreateAt { get; set; }
        [JsonPropertyName("mobile")]
        public string Mobile { get; set; } = string.Empty;
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;
        [JsonPropertyName("surname")]
        public string Surname { get; set; } = string.Empty;
        [JsonPropertyName("mobileConfirmation")]
        public MobileConfirmation MobileConfirmation { get; set; } = new();
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        [JsonPropertyName("salesCode")]
        public string SalesCode { get; set; } = string.Empty;
        [JsonPropertyName("businessName")]
        public string? BusinessName { get; set; }
        [JsonPropertyName("businessVat")]
        public string? BusinessVat { get; set; }
        [JsonPropertyName("businessAddress")]
        public string? BusinessAddress { get; set; }
    }

    public class MobileConfirmation
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("confirmed")]
        public bool Confirmed { get; set; }
        [JsonPropertyName("confirmationTime")]
        public DateTime? ConfirmationTime { get; set; }
        [JsonPropertyName("codeExpirationTime")]
        public DateTime? CodeExpirationTime { get; set; }
        [JsonPropertyName("resendOtpCount")]
        public int? ResendOtpCount { get; set; }
    }

    public class Token
    {
        [JsonPropertyName("token")]
        public string TokenValue { get; set; } = string.Empty;
        [JsonPropertyName("expiration")]
        public DateTime Expiration { get; set; }
    }
}
