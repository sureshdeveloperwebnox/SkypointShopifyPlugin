using System.Collections.Generic;
using System.Threading.Tasks;
using SkypointShopifyPlugin.Core.DTOs.Skypoint;

namespace SkypointShopifyPlugin.Core.Interfaces
{
    public interface ISkypointApiClient
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);
        Task<LoginResponse> LoginAsync(LoginRequest request, string baseUrl);

        Task<LoginResponse> RegisterAsync(RegisterRequest request);
        Task<LoginResponse> RegisterAsync(RegisterRequest request, string baseUrl);

        Task<List<RateResponse>> GetRatesAsync(RateRequest request, string authToken);
        Task<List<RateResponse>> GetRatesAsync(RateRequest request, string authToken, string baseUrl);

        Task<BookingResponse> CreateBookingAsync(BookingRequest request, string authToken);
        Task<BookingResponse> CreateBookingAsync(BookingRequest request, string authToken, string baseUrl);

        Task<TrackingResponse> TrackBookingAsync(string trackNo, string authToken);
        Task<TrackingResponse> TrackBookingAsync(string trackNo, string authToken, string baseUrl);

        Task<PudoPointResponse> GetSelectedPudoPointAsync(string guid, string authToken);
        Task<PudoPointResponse> GetSelectedPudoPointAsync(string guid, string authToken, string baseUrl);

        Task<WaybillDownloadResponse> DownloadWaybillAsync(string waybillNumber, string authToken);
        Task<WaybillDownloadResponse> DownloadWaybillAsync(string waybillNumber, string authToken, string baseUrl);

        Task<WaybillDownloadResponse> BulkLabelPrintAsync(List<string> bookingIds, string authToken);
        Task<WaybillDownloadResponse> BulkLabelPrintAsync(List<string> bookingIds, string authToken, string baseUrl);

        Task<BookingResponse> GetBookingDetailsAsync(string bookingId, string authToken);
        Task<BookingResponse> GetBookingDetailsAsync(string bookingId, string authToken, string baseUrl);

        Task<BookingResponse> ProcessBookingAsync(string trackNo, string authToken);
        Task<BookingResponse> ProcessBookingAsync(string trackNo, string authToken, string baseUrl);
    }
}
