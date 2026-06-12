using Checkout.Application;
using Checkout.Infrastructure.Stripe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Checkout.Tests;

/// <summary>
/// Wiring tests for the Stripe adapter: AddStripeCheckout registers the
/// gateway behind the port and rejects invalid configuration. No Stripe
/// calls are made — constructing the gateway only configures the client.
/// </summary>
public class StripeRegistrationTests
{
    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Stripe:SecretKey"] = "sk_test_placeholder",
        ["Stripe:DefaultSuccessUrl"] = "https://merchant.example.com/success",
        ["Stripe:DefaultCancelUrl"] = "https://merchant.example.com/cancel",
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        // AddLogging mirrors the host, which always registers logging; the
        // gateway takes an ILogger to record provider misconfigurations.
        return new ServiceCollection().AddLogging().AddStripeCheckout(configuration).BuildServiceProvider();
    }

    [Fact]
    public void Registers_the_stripe_gateway_behind_the_port()
    {
        using var provider = Build(ValidSettings());

        Assert.IsType<StripeCheckoutGateway>(provider.GetRequiredService<ICheckoutGateway>());
    }

    [Fact]
    public void Binds_options_from_the_stripe_section()
    {
        using var provider = Build(ValidSettings());

        var options = provider.GetRequiredService<IOptions<StripeOptions>>().Value;
        Assert.Equal("sk_test_placeholder", options.SecretKey);
        Assert.Equal("https://merchant.example.com/success", options.DefaultSuccessUrl);
        Assert.Equal("https://merchant.example.com/cancel", options.DefaultCancelUrl);
    }

    [Fact]
    public void Rejects_missing_secret_key()
    {
        var settings = ValidSettings();
        settings.Remove("Stripe:SecretKey");
        using var provider = Build(settings);

        var ex = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<ICheckoutGateway>);
        Assert.Contains("SecretKey", ex.Message);
    }

    [Theory]
    [InlineData("Stripe:DefaultSuccessUrl", "http://merchant.example.com/success", "DefaultSuccessUrl")]
    [InlineData("Stripe:DefaultCancelUrl", "http://merchant.example.com/cancel", "DefaultCancelUrl")]
    public void Rejects_non_https_default_urls(string key, string value, string field)
    {
        var settings = ValidSettings();
        settings[key] = value;
        using var provider = Build(settings);

        var ex = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<ICheckoutGateway>);
        Assert.Contains($"{field} must be an https URL", ex.Message);
    }
}
