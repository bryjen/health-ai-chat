using System.Net.Http.Headers;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Services;

/// <summary>
/// HTTP message handler that automatically adds the authentication token to requests.
/// </summary>
public class TokenProviderHttpMessageHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;

    public TokenProviderHttpMessageHandler(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
