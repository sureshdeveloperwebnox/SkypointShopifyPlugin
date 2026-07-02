using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Cryptography;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;
        private readonly IConfiguration _configuration;

        public AuthController(
            ILogger<AuthController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore,
            IConfiguration configuration)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
            _configuration = configuration;
        }

        /// <summary>
        /// Login with Skypoint credentials.
        /// Pass ?shop=yourstore.myshopify.com so the server caches the token
        /// per shop — used automatically by the carrier rate callback.
        /// The token is returned in the JSON body; the client stores it in
        /// sessionStorage (lives for the tab session only, never written to disk).
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            _logger.LogInformation("Login attempt for: {Username}", request.Username);

            try
            {
                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Pwd))
                    return BadRequest(new { error = "Username and password are required" });

                var loginResponse = await _skypointApiClient.LoginAsync(request);

                if (loginResponse == null || string.IsNullOrEmpty(loginResponse.Token?.TokenValue))
                {
                    _logger.LogWarning("Login failed for: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid username or password" });
                }

                // Cache credentials + token on the server keyed by shop.
                // This is how the carrier rate callback (server→Skypoint) gets its token
                // without any per-request auth header.
                var shop = Request.Query["shop"].ToString();
                if (string.IsNullOrEmpty(shop))
                    shop = Request.Headers["X-Shop-Domain"].ToString();

                if (!string.IsNullOrEmpty(shop))
                {
                    shop = shop.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                    _skypointTokenStore.SaveCredentials(shop, request.Username, request.Pwd);
                    _skypointTokenStore.SaveToken(shop, loginResponse.Token.TokenValue, loginResponse.Token.Expiration, loginResponse.Id);
                    _logger.LogInformation("Skypoint token cached for shop: {Shop}", shop);
                }

                var jwtToken = GenerateLocalJwtToken(request.Username, loginResponse.Role ?? "client", loginResponse.Id ?? "", shop);

                _logger.LogInformation("Login successful for: {Username}", request.Username);

                return Ok(new
                {
                    success = true,
                    message = "Login successful",
                    token      = jwtToken,
                    expiration = DateTime.UtcNow.AddDays(7),
                    user = new
                    {
                        id         = loginResponse.Id,
                        username   = loginResponse.Username,
                        email      = loginResponse.Owner?.Email,
                        firstName  = loginResponse.Owner?.FirstName,
                        lastName   = loginResponse.Owner?.Surname,
                        role       = loginResponse.Role,
                        accountNo  = loginResponse.AccountNo,
                        salesCode  = loginResponse.Owner?.SalesCode
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for: {Username}", request.Username);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            _logger.LogInformation("Registration for: {Email}", request.Email);

            try
            {
                if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                    return BadRequest(new { error = "Email and password are required" });

                var skypointReq = new Core.DTOs.Skypoint.RegisterRequest
                {
                    Username  = request.Email,
                    Pwd       = request.Password,
                    Email     = request.Email,
                    FirstName = request.FirstName,
                    LastName  = request.LastName,
                    Phone     = request.Phone
                };

                var reg = await _skypointApiClient.RegisterAsync(skypointReq);

                // Cache token per shop if shop is provided in query/headers
                var shop = Request.Query["shop"].ToString();
                if (string.IsNullOrEmpty(shop))
                    shop = Request.Headers["X-Shop-Domain"].ToString();

                if (!string.IsNullOrEmpty(shop))
                {
                    shop = shop.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                    _skypointTokenStore.SaveCredentials(shop, request.Email, request.Password);
                    _skypointTokenStore.SaveToken(shop, reg.Token.TokenValue, reg.Token.Expiration, reg.Id);
                }

                var jwtToken = GenerateLocalJwtToken(request.Email, reg.Role ?? "client", reg.Id ?? "", shop);

                return Ok(new
                {
                    success    = true,
                    message    = "Registration successful",
                    token      = jwtToken,
                    expiration = DateTime.UtcNow.AddDays(7),
                    user = new
                    {
                        id        = reg.Id,
                        username  = reg.Username,
                        email     = reg.Owner?.Email,
                        firstName = reg.Owner?.FirstName,
                        lastName  = reg.Owner?.Surname,
                        role      = reg.Role,
                        accountNo = reg.AccountNo
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for: {Email}", request.Email);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>Logout — client clears sessionStorage; nothing to do server-side.</summary>
        [HttpPost("logout")]
        public IActionResult Logout() => Ok(new { success = true });

        private string GenerateLocalJwtToken(string username, string role, string userId, string? shop)
        {
            var encryptionKey = _configuration["EncryptionKey"] ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "SkypointShopifyPluginDefaultSecretKey32BytesForSigningTokens!";
            byte[] jwtKeyBytes;
            using (var sha256 = SHA256.Create())
            {
                jwtKeyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey));
            }
            var key = new SymmetricSecurityKey(jwtKeyBytes);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, username),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)
            };

            if (!string.IsNullOrEmpty(shop))
            {
                claims.Add(new System.Security.Claims.Claim("shop", shop));
            }

            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: creds
            );

            return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public class RegisterRequest
    {
        public string Email     { get; set; } = string.Empty;
        public string Password  { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName  { get; set; } = string.Empty;
        public string Phone     { get; set; } = string.Empty;
    }
}
