using MediatR;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Application.Features.Skypoint.Commands
{
    public class LoginCommandHandler : IRequestHandler<LoginCommand, Response<LoginResponse>>
    {
        private readonly ISkypointApiClient _apiClient;
        private readonly ILogger<LoginCommandHandler> _logger;

        public LoginCommandHandler(ISkypointApiClient apiClient, ILogger<LoginCommandHandler> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<Response<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var loginRequest = new LoginRequest
                {
                    Username = request.Username,
                    Pwd = request.Password
                };

                var result = await _apiClient.LoginAsync(loginRequest);
                return Response<LoginResponse>.Success(result, "Login successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
                return Response<LoginResponse>.Fail($"Login failed: {ex.Message}", 500);
            }
        }
    }
}
