using Checkout.Domain;

namespace Checkout.Application;

/// <summary>
/// Use case: create a hosted checkout session for a customer and an order —
/// either a single offer, or a multi-line bundle. Translates the caller-facing
/// command into a validated domain order and hands it to the payment gateway.
/// </summary>
public sealed class CreateCheckoutSession(ICheckoutGateway gateway)
{
    /// <summary>Stripe caps idempotency keys at 255 characters; stay below it
    /// so the per-call suffixes the gateway appends still fit.</summary>
    public const int MaxIdempotencyKeyLength = 200;

    public async Task<CheckoutSession> ExecuteAsync(
        CreateCheckoutSessionCommand command,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        // A blank header must not become a real key: every such request would
        // share the same Stripe idempotency key and collide across callers.
        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey is { Length: > MaxIdempotencyKeyLength })
        {
            throw new DomainValidationException(
                $"Idempotency-Key must not exceed {MaxIdempotencyKeyLength} characters");
        }

        var customer = Customer.Create(
            command.Customer?.Email,
            command.Customer?.Name,
            command.Customer?.Phone,
            command.Customer?.ExternalId,
            command.Customer?.ProviderCustomerId);

        var mode = ParseMode(command.Mode);

        var order = CheckoutOrder.Create(
            customer,
            ParseLines(command, mode),
            mode,
            ParseUrl(command.SuccessUrl, "successUrl"),
            ParseUrl(command.CancelUrl, "cancelUrl"),
            command.Metadata,
            command.AutomaticTax ?? false);

        return await gateway.CreateSessionAsync(order, idempotencyKey, cancellationToken);
    }

    private static List<OrderLine> ParseLines(CreateCheckoutSessionCommand command, CheckoutMode mode)
    {
        var alternatives = new[] { command.PriceId is not null, command.Offer is not null, command.LineItems is not null };
        if (alternatives.Count(set => set) != 1)
            throw new DomainValidationException("exactly one of priceId, offer or lineItems is required");

        if (command.LineItems is null)
            return [OrderLine.Create(ParsePricing(command.PriceId, command.Offer, mode), command.Quantity)];

        if (command.Quantity is not null)
            throw new DomainValidationException("quantity belongs on each line item when lineItems is used");

        return
        [
            .. command.LineItems.Select(item =>
            {
                if (item.PriceId is not null == (item.Offer is not null))
                    throw new DomainValidationException("each line item requires exactly one of priceId or offer");
                return OrderLine.Create(ParsePricing(item.PriceId, item.Offer, mode), item.Quantity);
            }),
        ];
    }

    private static Pricing ParsePricing(string? priceId, OfferDto? offer, CheckoutMode mode) =>
        priceId is not null
            ? Pricing.FromPriceId(priceId)
            : Pricing.FromOffer(
                offer!.Name,
                offer.AmountCents,
                offer.Description,
                offer.Currency,
                // An unspecified `recurring` follows the order mode, so a plain
                // offer in a payment order stays a one-time charge.
                new OfferSchedule(
                    offer.Recurring ?? (mode == CheckoutMode.Subscription),
                    ParseInterval(offer.Interval), offer.IntervalCount, offer.TrialDays));

    private static CheckoutMode ParseMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        null or "subscription" => CheckoutMode.Subscription,
        "payment" => CheckoutMode.Payment,
        _ => throw new DomainValidationException($"unknown mode '{mode}' (expected 'subscription' or 'payment')"),
    };

    private static BillingInterval? ParseInterval(string? interval) => interval?.ToLowerInvariant() switch
    {
        null => null,
        "day" => BillingInterval.Day,
        "week" => BillingInterval.Week,
        "month" => BillingInterval.Month,
        "year" => BillingInterval.Year,
        _ => throw new DomainValidationException($"unknown interval '{interval}' (expected 'day', 'week', 'month' or 'year')"),
    };

    private static Uri? ParseUrl(string? url, string field)
    {
        if (url is null)
            return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
            return parsed;
        throw new DomainValidationException($"{field} must be an absolute https URL");
    }
}

public sealed record CreateCheckoutSessionCommand(
    CustomerDto? Customer,
    string? PriceId,
    OfferDto? Offer,
    List<LineItemDto>? LineItems,
    long? Quantity,
    string? Mode,
    string? SuccessUrl,
    string? CancelUrl,
    Dictionary<string, string>? Metadata,
    bool? AutomaticTax = null);

public sealed record CustomerDto(
    string? Email,
    string? Name,
    string? Phone,
    string? ExternalId,
    string? ProviderCustomerId = null);

public sealed record LineItemDto(
    string? PriceId,
    OfferDto? Offer,
    long? Quantity);

public sealed record OfferDto(
    string? Name,
    string? Description,
    long AmountCents,
    string? Currency,
    string? Interval,
    long? IntervalCount,
    long? TrialDays,
    bool? Recurring);
