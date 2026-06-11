using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Checkout.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Checkout.Tests;

/// <summary>
/// The demo front end (wwwroot) and its key-less /demo/checkout-sessions
/// endpoint exist only in Development: the page never handles the API key —
/// the demo endpoint stands in for the partner backend that would hold it.
/// In any other environment neither is mapped.
/// </summary>
public class DemoPageTests
{
    private static WebApplicationFactory<Program> CreateFactory(string? environment = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (environment is not null)
                builder.UseSetting("environment", environment);
            builder.UseSetting("Api:Key", "test-api-key");
            builder.UseSetting("Stripe:SecretKey", "sk_test_placeholder");
            builder.UseSetting("Stripe:DefaultSuccessUrl", "https://merchant.example.com/success?session_id={CHECKOUT_SESSION_ID}");
            builder.UseSetting("Stripe:DefaultCancelUrl", "https://merchant.example.com/cancel");
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ICheckoutGateway>(new FakeCheckoutGateway())));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        // https base address so UseHttpsRedirection doesn't bounce the request.
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Theory]
    [InlineData("/")]
    [InlineData("/success.html")]
    [InlineData("/cancel.html")]
    public async Task Serves_demo_pages_in_development(string path)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Demo_page_posts_to_the_demo_endpoint_and_never_sees_the_api_key()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var html = await client.GetStringAsync("/");

        Assert.Contains("/demo/checkout-sessions", html);
        Assert.DoesNotContain("X-Api-Key", html);
        Assert.Contains("{CHECKOUT_SESSION_ID}", html);
        // Tax rides along as a one-time line, mirroring Starlink's tax step.
        Assert.Contains("name: 'Tax'", html);
        // The partner correlation key is sent so the session carries
        // client_reference_id back through the completed-checkout webhook.
        Assert.Contains("externalId", html);
    }

    [Fact]
    public async Task Demo_endpoint_creates_a_session_without_an_api_key()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/demo/checkout-sessions", new
        {
            customer = new { email = "jane@example.com" },
            offer = new { name = "Residential Internet", amountCents = 12000 },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cs_fake_123", body.GetProperty("sessionId").GetString());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Demo_endpoint_does_not_exist_outside_development(string environment)
    {
        using var factory = CreateFactory(environment);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/demo/checkout-sessions", new
        {
            customer = new { email = "jane@example.com" },
            offer = new { name = "Residential Internet", amountCents = 12000 },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Demo_pages_are_not_served_outside_development()
    {
        using var factory = CreateFactory("Production");
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
