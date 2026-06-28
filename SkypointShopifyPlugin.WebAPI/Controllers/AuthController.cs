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
        /// Pass ?shop=yourstore.myshopify.com so the token gets cached per shop
        /// and used automatically by the carrier rate callback.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            _logger.LogInformation("Login request for username: {Username}", request.Username);

            try
            {
                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Pwd))
                    return BadRequest(new { error = "Username and password are required" });

                var loginResponse = await _skypointApiClient.LoginAsync(request);

                if (loginResponse == null || string.IsNullOrEmpty(loginResponse.Token?.TokenValue))
                {
                    _logger.LogError("Login failed for username: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid username or password" });
                }

                _logger.LogInformation("Login successful for username: {Username}", request.Username);

                // Cache credentials + token per shop so carrier callback works
                // without any hardcoded values or file storage
                var shop = Request.Query["shop"].ToString();
                if (string.IsNullOrEmpty(shop))
                    shop = Request.Headers["X-Shop-Domain"].ToString();

                if (!string.IsNullOrEmpty(shop))
                {
                    shop = shop.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                    // Store credentials so token can be refreshed automatically when it expires
                    _skypointTokenStore.SaveCredentials(shop, request.Username, request.Pwd);
                    // Store the current token
                    _skypointTokenStore.SaveToken(shop, loginResponse.Token.TokenValue, loginResponse.Token.Expiration);
                    _logger.LogInformation("Skypoint token cached for shop: {Shop}", shop);
                }

                return Ok(new
                {
                    success = true,
                    token = loginResponse.Token.TokenValue,
                    expiration = loginResponse.Token.Expiration,
                    user = new
                    {
                        id = loginResponse.Id,
                        username = loginResponse.Username,
                        email = loginResponse.Owner?.Email,
                        firstName = loginResponse.Owner?.FirstName,
                        lastName = loginResponse.Owner?.Surname,
                        role = loginResponse.Role,
                        accountNo = loginResponse.AccountNo,
                        salesCode = loginResponse.Owner?.SalesCode
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for username: {Username}", request.Username);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            _logger.LogInformation("Registration request for email: {Email}", request.Email);

            try
            {
                if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                    return BadRequest(new { error = "Email and password are required" });

                var skypointRegisterRequest = new Core.DTOs.Skypoint.RegisterRequest
                {
                    Username = request.Email,
                    Pwd = request.Password,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Phone = request.Phone
                };

                var registerResponse = await _skypointApiClient.RegisterAsync(skypointRegisterRequest);

                if (registerResponse == null || string.IsNullOrEmpty(registerResponse.Token?.TokenValue))
                {
                    _logger.LogError("Registration failed for email: {Email}", request.Email);
                    return BadRequest(new { error = "Registration failed" });
                }

                _logger.LogInformation("Registration successful for email: {Email}", request.Email);

                return Ok(new
                {
                    success = true,
                    token = registerResponse.Token.TokenValue,
                    expiration = registerResponse.Token.Expiration,
                    user = new
                    {
                        id = registerResponse.Id,
                        username = registerResponse.Username,
                        email = registerResponse.Owner?.Email,
                        firstName = registerResponse.Owner?.FirstName,
                        lastName = registerResponse.Owner?.Surname,
                        role = registerResponse.Role,
                        accountNo = registerResponse.AccountNo
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for email: {Email}", request.Email);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }
}
