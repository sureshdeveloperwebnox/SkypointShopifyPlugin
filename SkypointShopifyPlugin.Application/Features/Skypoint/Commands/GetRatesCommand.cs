using MediatR;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Application.Features.Skypoint.Commands
{
    public record GetRatesCommand : IRequest<Response<List<RateResponse>>>
    {
        public string AuthToken { get; set; } = string.Empty;
        public string PickUpSuburb { get; set; } = string.Empty;
        public string PickUpPostalCode { get; set; } = string.Empty;
        public string DropOffSuburb { get; set; } = string.Empty;
        public string DropOffPostalCode { get; set; } = string.Empty;
        public List<ParcelDimension> ParcelsDims { get; set; } = new();
    }
}
