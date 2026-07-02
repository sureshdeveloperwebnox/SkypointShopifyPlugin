using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace SkypointShopifyPlugin.WebUI.Handlers
{
    /// <summary>
    /// Intercepts outgoing HttpClient requests and injects the JWT Bearer token from ProtectedSessionStorage.
    /// Handles pre-rendering constraints gracefully.
    /// </summary>
    public class TokenAuthorizationHandler : DelegatingHandler
    {
        private readonly ProtectedSessionStorage _sessionStorage;

        public TokenAuthorizationHandler(ProtectedSessionStorage sessionStorage)
        {
            _sessionStorage = sessionStorage;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                var tokenResult = await _sessionStorage.GetAsync<string>("skypoint_token");
                if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
                }
            }
            catch (InvalidOperationException)
            {
                // JSInterop is not available during static pre-rendering. 
                // Ignore and proceed without the authorization header.
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
