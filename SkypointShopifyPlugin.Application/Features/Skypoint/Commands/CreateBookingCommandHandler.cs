using MediatR;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Application.Common;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Application.Features.Skypoint.Commands
{
    public class CreateBookingCommandHandler : IRequestHandler<CreateBookingCommand, Response<BookingResponse>>
    {
        private readonly ISkypointApiClient _apiClient;
        private readonly ILogger<CreateBookingCommandHandler> _logger;

        public CreateBookingCommandHandler(ISkypointApiClient apiClient, ILogger<CreateBookingCommandHandler> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<Response<BookingResponse>> Handle(CreateBookingCommand request, CancellationToken cancellationToken)
        {
            try
            {
                var bookingRequest = new BookingRequest
                {
                    UserId = request.UserId,
                    PickUpAddress = request.PickUpAddress,
                    DropOffAddress = request.DropOffAddress,
                    FromSuburb = request.FromSuburb,
                    ToSuburb = request.ToSuburb,
                    PickUpPCode = request.PickUpPCode,
                    DropOffPCode = request.DropOffPCode,
                    Comment = request.Comment,
                    Province = request.Province,
                    DestinationProvince = request.DestinationProvince,
                    DropOff = request.DropOff,
                    PickUp = request.PickUp,
                    Type = request.Type,
                    PickUpDate = request.PickUpDate,
                    PickUpTime = request.PickUpTime,
                    ParcelDimensions = request.ParcelDimensions,
                    PickUpCity = request.PickUpCity,
                    DropOffCity = request.DropOffCity,
                    PickUpZip = request.PickUpZip,
                    DropOffZip = request.DropOffZip,
                    ShipmentType = request.ShipmentType,
                    ToCounterCode = request.ToCounterCode,
                    ToCounterName = request.ToCounterName,
                    SaIdNumber = request.SaIdNumber,
                    PickUpCountry = request.PickUpCountry
                };

                var result = await _apiClient.CreateBookingAsync(bookingRequest, request.AuthToken);
                return Response<BookingResponse>.Success(result, $"Booking created with tracking number: {result.TrackNo}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating booking");
                return Response<BookingResponse>.Fail($"Failed to create booking: {ex.Message}", 500);
            }
        }
    }
}
