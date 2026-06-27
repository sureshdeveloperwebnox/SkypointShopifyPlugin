namespace SkypointShopifyPlugin.Application.Common
{
    public class Response<T>
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
        public int StatusCode { get; set; } = 200;

        public static Response<T> Success(T data, string message = "Operation successful")
        {
            return new Response<T> { Succeeded = true, Data = data, Message = message, StatusCode = 200 };
        }

        public static Response<T> Fail(string message, int statusCode = 400)
        {
            return new Response<T> { Succeeded = false, Message = message, StatusCode = statusCode };
        }
    }
}
