using MediatR;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Application.Features.Skypoint.Commands
{
    public class GetRatesCommandHandler : IRequestHandler<GetRatesCommand, Response<List<RateResponse>>>
    {
        private readonly ISkypointApiClient _apiClient;
        private readonly ILogger<GetRatesCommandHandler> _logger;

        public GetRatesCommandHandler(ISkypointApiClient apiClient, ILogger<GetRatesCommandHandler> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<Response<List<RateResponse>>> Handle(GetRatesCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var rateRequest = new RateRequest
                {
                    PickUpSuburb = request.PickUpSuburb,
                    PickUpPostalCode = request.PickUpPostalCode,
                    DropOffSuburb = request.DropOffSuburb,
                    DropOverPostalCode = request.DropOffPostalCode,
                    ParcelsDims = request.ParcelsDims
                };

                var result = await _apiClient.GetRatesAsync(rateRequest, request.AuthToken);
                return Response<List<RateResponse>>.Success(result, "Rates retrieved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting rates");
                return Response<List<RateResponse>>.Fail($"Failed to get rates: {ex.Message}", 500);
            }
        }
    }
}
