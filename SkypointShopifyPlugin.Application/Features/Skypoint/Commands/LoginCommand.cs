using MediatR;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Application.Features.Skypoint.Commands
{
    public record LoginCommand : IRequest<Response<LoginResponse>>
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
