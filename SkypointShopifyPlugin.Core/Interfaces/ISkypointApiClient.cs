using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface ISkypointApiClient
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);
        Task<List<RateResponse>> GetRatesAsync(RateRequest request, string authToken);
        Task<BookingResponse> CreateBookingAsync(BookingRequest request, string authToken);
    }
}
