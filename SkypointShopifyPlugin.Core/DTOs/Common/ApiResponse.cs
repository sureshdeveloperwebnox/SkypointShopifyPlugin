namespace SkypointShopifyPlugin.Core.DTOs.Common
{
    /// <summary>
    /// A standardized envelope for all API responses.
    /// Provides consistent output structure for frontend consumption.
    /// </summary>
    /// <typeparam name="T">The type of data returned in the response.</typeparam>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public string? ErrorDetails { get; set; }

        public ApiResponse()
        {
        }

        public ApiResponse(T data, string? message = null)
        {
            Success = true;
            Data = data;
            Message = message;
        }

        public ApiResponse(string message, string? errorDetails = null)
        {
            Success = false;
            Message = message;
            ErrorDetails = errorDetails;
        }

        public static ApiResponse<T> CreateSuccess(T data, string? message = null)
            => new ApiResponse<T>(data, message);

        public static ApiResponse<T> CreateError(string message, string? errorDetails = null)
            => new ApiResponse<T>(message, errorDetails);
    }
}
