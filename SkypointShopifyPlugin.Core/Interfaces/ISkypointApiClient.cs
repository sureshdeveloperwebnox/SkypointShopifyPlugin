using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface ISkypointApiClient
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);
        Task<LoginResponse> RegisterAsync(RegisterRequest request);
        Task<List<RateResponse>> GetRatesAsync(RateRequest request, string authToken);
        Task<BookingResponse> CreateBookingAsync(BookingRequest request, string authToken);
        Task<TrackingResponse> TrackBookingAsync(string trackNo, string authToken);
        Task<PudoPointResponse> GetSelectedPudoPointAsync(string guid, string authToken);
        Task<WaybillDownloadResponse> DownloadWaybillAsync(string waybillNumber, string authToken);
        Task<WaybillDownloadResponse> BulkLabelPrintAsync(List<string> bookingIds, string authToken);
        Task<BookingResponse> GetBookingDetailsAsync(string bookingId, string authToken);
        Task<BookingResponse> ProcessBookingAsync(string trackNo, string authToken);
    }
}
