using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Application;
using Checkout.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Checkout.Tests;

/// <summary>
/// End-to-end tests of the HTTP surface: API-key auth and the mapping of
/// outcomes to status codes (200 / 400 domain / 502 gateway). The Stripe
/// gateway is replaced with a fake, so no network or Stripe key is needed.
/// </summary>
public class CheckoutApiTests
{
    private const string ApiKey = "test-api-key";

    private static WebApplicationFactory<Program> CreateFactory(ICheckoutGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Api:Key", ApiKey);
            builder.UseSetting("Stripe:SecretKey", "sk_test_placeholder");
            builder.UseSetting("Stripe:DefaultSuccessUrl", "https://merchant.example.com/success?session_id={CHECKOUT_SESSION_ID}");
            builder.UseSetting("Stripe:DefaultCancelUrl", "https://merchant.example.com/cancel");
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(gateway)));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        // https base address so UseHttpsRedirection doesn't bounce the request.
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static HttpRequestMessage Request(object body, string? apiKey = ApiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout-sessions")
        {
            Content = JsonContent.Create(body),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }

    private static object ValidBody() => new
    {
        customer = new { email = "jane@example.com" },
        offer = new { name = "Residential Internet", amountCents = 12000 },
    };

    [Fact]
    public async Task Health_check_requires_no_api_key()
    {
        using var factory = CreateFactory(new FakeCheckoutGateway());
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Fails_at_startup_when_api_key_is_not_configured()
    {
        // appsettings.json ships an empty Api:Key on purpose; a deployment that
        // forgets to override it must fail closed at boot, not run with a known key.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Api:Key", "");
            builder.UseSetting("Stripe:SecretKey", "sk_test_placeholder");
        });

        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);
        Assert.Contains("Key", exception.Message);
    }

    [Fact]
    public async Task Rejects_request_without_api_key()
    {
        using var factory = CreateFactory(new FakeCheckoutGateway());
        using var client = CreateClient(factory);

        var response = await client.SendAsync(Request(ValidBody(), apiKey: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_request_with_wrong_api_key()
    {
        using var factory = CreateFactory(new FakeCheckoutGateway());
        using var client = CreateClient(factory);

        var response = await client.SendAsync(Request(ValidBody(), apiKey: "wrong-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creates_session_and_returns_its_details()
    {
        using var factory = CreateFactory(new FakeCheckoutGateway());
        using var client = CreateClient(factory);

        var response = await client.SendAsync(Request(ValidBody()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cs_fake_123", body.GetProperty("sessionId").GetString());
        Assert.Equal("https://checkout.example.com/c/pay/cs_fake_123", body.GetProperty("checkoutUrl").GetString());
        Assert.Equal("cus_fake_123", body.GetProperty("customerId").GetString());
        Assert.True(body.TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task Maps_domain_validation_failure_to_400_with_the_message()
    {
        using var factory = CreateFactory(new FakeCheckoutGateway());
        using var client = CreateClient(factory);

        var response = await client.SendAsync(Request(new { customer = new { email = "jane@example.com" } }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("exactly one of priceId, offer or lineItems", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Maps_gateway_failure_to_502_with_the_message()
    {
        using var factory = CreateFactory(new ThrowingCheckoutGateway());
        using var client = CreateClient(factory);

        var response = await client.SendAsync(Request(ValidBody()));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("the payment provider rejected the request", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Maps_unexpected_failures_to_500_with_the_same_error_envelope()
    {
        using var factory = CreateFactory(new ThrowingCheckoutGateway(
            new InvalidOperationException("internal detail that must not leak")));
        using var client = CreateClient(factory);

        var response = await client.SendAsync(Request(ValidBody()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("an unexpected error occurred", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Forwards_the_idempotency_key_header_to_the_gateway()
    {
        var gateway = new FakeCheckoutGateway();
        using var factory = CreateFactory(gateway);
        using var client = CreateClient(factory);

        var request = Request(ValidBody());
        request.Headers.Add("Idempotency-Key", "order-789");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("order-789", gateway.LastIdempotencyKey);
    }

    [Fact]
    public async Task Treats_a_blank_idempotency_key_header_as_absent()
    {
        var gateway = new FakeCheckoutGateway();
        using var factory = CreateFactory(gateway);
        using var client = CreateClient(factory);

        var request = Request(ValidBody());
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "   ");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(gateway.LastIdempotencyKey);
    }

    private sealed class ThrowingCheckoutGateway(Exception? exception = null) : ICheckoutGateway
    {
        public Task<CheckoutSession> CreateSessionAsync(
            CheckoutOrder order, string? idempotencyKey, CancellationToken cancellationToken) =>
            throw exception ?? new CheckoutGatewayException(
                "the payment provider rejected the request",
                new InvalidOperationException("provider error detail"));
    }
}
