using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Checkout.Api;

/// <summary>Rejects requests that don't carry the shared secret in X-Api-Key.</summary>
public sealed class ApiKeyEndpointFilter(IOptions<ApiKeyOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), options.Value.Key))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

public sealed class ApiKeyOptions
{
    public const string SectionName = "Api";

    /// <summary>Shared secret callers must send in the X-Api-Key header.</summary>
    [Required]
    public string Key { get; init; } = "";
}
