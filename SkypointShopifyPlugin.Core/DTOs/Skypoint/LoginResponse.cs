namespace SkypointShopifyPlugin.Core.DTOs.Skypoint
{
    public class LoginResponse
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public string Username { get; set; } = string.Empty;
        public string UserType { get; set; } = string.Empty;
        public Owner Owner { get; set; } = new();
        public Token Token { get; set; } = new();
        public string Role { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public string AccountNo { get; set; } = string.Empty;
        public string CanAddStickers { get; set; } = string.Empty;
    }

    public class Owner
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreateAt { get; set; }
        public string Mobile { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public MobileConfirmation MobileConfirmation { get; set; } = new();
        public string Email { get; set; } = string.Empty;
        public string SalesCode { get; set; } = string.Empty;
        public string? BusinessName { get; set; }
        public string? BusinessVat { get; set; }
        public string? BusinessAddress { get; set; }
    }

    public class MobileConfirmation
    {
        public string Code { get; set; } = string.Empty;
        public bool Confirmed { get; set; }
        public DateTime? ConfirmationTime { get; set; }
        public DateTime CodeExpirationTime { get; set; }
        public int? ResendOtpCount { get; set; }
    }

    public class Token
    {
        public string TokenValue { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
    }
}
