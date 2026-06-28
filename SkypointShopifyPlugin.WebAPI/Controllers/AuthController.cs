using Microsoft.AspNetCore.Mvc;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.WebAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly ISkypointApiClient _skypointApiClient;
        private readonly ISkypointTokenStore _skypointTokenStore;

        public AuthController(
            ILogger<AuthController> logger,
            ISkypointApiClient skypointApiClient,
            ISkypointTokenStore skypointTokenStore)
        {
            _logger = logger;
            _skypointApiClient = skypointApiClient;
            _skypointTokenStore = skypointTokenStore;
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
                    _skypointTokenStore.SaveToken(shop, loginResponse.Token.TokenValue, loginResponse.Token.Expiration);
                    _logger.LogInformation("Skypoint token cached for shop: {Shop}", shop);
                }

                _logger.LogInformation("Login successful for: {Username}", request.Username);

                return Ok(new
                {
                    success = true,
                    message = "Login successful",
                    // Token returned to client — stored in sessionStorage only (not localStorage/disk)
                    token      = loginResponse.Token.TokenValue,
                    expiration = loginResponse.Token.Expiration,
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

                if (reg == null || string.IsNullOrEmpty(reg.Token?.TokenValue))
                    return BadRequest(new { error = "Registration failed" });

                return Ok(new
                {
                    success    = true,
                    message    = "Registration successful",
                    token      = reg.Token.TokenValue,
                    expiration = reg.Token.Expiration,
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
