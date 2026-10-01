using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

internal sealed class McpAuthenticationMiddleware(
    RequestDelegate next,
    IOptions<McpOptions> options)
{
    private readonly string? configuredToken = options.Value.AuthToken;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuredToken)
            || HasValidBearerToken(context.Request))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
    }

    private bool HasValidBearerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authorization)
            || !AuthenticationHeaderValue.TryParse(authorization.ToString(), out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return false;
        }

        var suppliedToken = Encoding.UTF8.GetBytes(header.Parameter);
        var expectedToken = Encoding.UTF8.GetBytes(configuredToken!);

        return CryptographicOperations.FixedTimeEquals(suppliedToken, expectedToken);
    }
}