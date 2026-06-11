using Checkout.Domain;

namespace Checkout.Application;

/// <summary>
/// Outbound port to the payment provider. Implemented in the infrastructure
/// layer (e.g. by Stripe); the application core depends only on this contract.
/// </summary>
public interface ICheckoutGateway
{
    Task<CheckoutSession> CreateSessionAsync(CheckoutOrder order, CancellationToken cancellationToken);
}

/// <summary>A hosted checkout session created by the payment provider.</summary>
public sealed record CheckoutSession(
    string SessionId,
    Uri CheckoutUrl,
    string CustomerId,
    DateTime ExpiresAtUtc);

/// <summary>Thrown when the payment provider rejects a request.</summary>
public sealed class CheckoutGatewayException(string message, Exception? inner = null)
    : Exception(message, inner);
