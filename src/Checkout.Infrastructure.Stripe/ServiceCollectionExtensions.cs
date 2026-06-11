using Checkout.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Infrastructure.Stripe;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers Stripe as the payment provider behind <see cref="ICheckoutGateway"/>.</summary>
    public static IServiceCollection AddStripeCheckout(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ICheckoutGateway, StripeCheckoutGateway>();
        return services;
    }
}
