namespace Checkout.Domain;

/// <summary>
/// What the customer is buying and at what price: either a reference to a
/// price pre-configured in the payment provider, or an ad-hoc offer defined
/// entirely by the caller (no provider-side configuration needed).
/// </summary>
public abstract record Pricing
{
    public static Pricing FromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
            throw new DomainValidationException("priceId must be non-empty");

        return new ProviderPrice(priceId);
    }

    public static Pricing FromOffer(
        string? name,
        long amountCents,
        string? description = null,
        string? currency = null,
        OfferSchedule? schedule = null)
    {
        var recurring = schedule?.Recurring ?? true;
        var interval = schedule?.Interval;
        var intervalCount = schedule?.IntervalCount;
        var trialDays = schedule?.TrialDays;

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("offer.name is required");
        if (amountCents <= 0)
            throw new DomainValidationException("offer.amountCents must be positive");
        if (intervalCount is < 1)
            throw new DomainValidationException("offer.intervalCount must be at least 1");
        if (trialDays is < 1)
            throw new DomainValidationException("offer.trialDays must be at least 1");
        if (!recurring && (interval is not null || intervalCount is not null || trialDays is not null))
        {
            throw new DomainValidationException(
                "offer.interval, intervalCount and trialDays only apply to recurring offers");
        }

        return new AdHocOffer(
            name, description, amountCents,
            string.IsNullOrWhiteSpace(currency) ? "usd" : currency.ToLowerInvariant(),
            recurring ? interval ?? BillingInterval.Month : null,
            recurring ? intervalCount ?? 1 : null,
            trialDays);
    }
}

/// <summary>A price configured in the payment provider (e.g. a Stripe Price ID).</summary>
public sealed record ProviderPrice(string PriceId) : Pricing;

/// <summary>
/// An offer priced by the caller at request time. A null <see cref="Interval"/>
/// means a one-time charge (e.g. shipping inside a subscription order).
/// </summary>
public sealed record AdHocOffer(
    string Name,
    string? Description,
    long AmountCents,
    string Currency,
    BillingInterval? Interval,
    long? IntervalCount,
    long? TrialDays) : Pricing
{
    public bool IsRecurring => Interval is not null;
}

/// <summary>
/// Billing schedule for a recurring offer.  Pass <c>null</c> (or omit) to use the default
/// recurring-monthly schedule; set <see cref="Recurring"/> to <c>false</c> for a one-time charge.
/// </summary>
public sealed record OfferSchedule(
    bool Recurring = true,
    BillingInterval? Interval = null,
    long? IntervalCount = null,
    long? TrialDays = null);

public enum BillingInterval
{
    Day,
    Week,
    Month,
    Year,
}
