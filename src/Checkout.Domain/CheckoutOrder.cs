namespace Checkout.Domain;

/// <summary>
/// A request to check out a customer for one or more order lines. The aggregate
/// root of this (very small) domain; enforces its own invariants on creation.
/// </summary>
public sealed class CheckoutOrder
{
    private CheckoutOrder(
        Customer customer,
        IReadOnlyList<OrderLine> lines,
        CheckoutMode mode,
        Uri? successUrl,
        Uri? cancelUrl,
        IReadOnlyDictionary<string, string>? metadata,
        bool automaticTax)
    {
        Customer = customer;
        Lines = lines;
        Mode = mode;
        SuccessUrl = successUrl;
        CancelUrl = cancelUrl;
        Metadata = metadata;
        AutomaticTax = automaticTax;
    }

    public Customer Customer { get; }
    public IReadOnlyList<OrderLine> Lines { get; }
    public CheckoutMode Mode { get; }
    public Uri? SuccessUrl { get; }
    public Uri? CancelUrl { get; }
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// Have the payment provider compute and collect tax at checkout, based on
    /// the address the customer enters there. Line amounts are tax-exclusive.
    /// </summary>
    public bool AutomaticTax { get; }

    /// <summary>
    /// Ceiling on lines per order. Real orders here are a service plan plus a
    /// few add-ons; far more than that is a caller bug. Stripe's own session
    /// limit is higher (100) — this fails the obviously wrong case fast.
    /// </summary>
    public const int MaxLineCount = 20;

    /// <summary>
    /// Convenience for the common single-offer subscription order. Payment-mode
    /// orders go through the line-list overload.
    /// </summary>
    public static CheckoutOrder Create(
        Customer customer,
        Pricing pricing,
        long? quantity = null,
        Uri? successUrl = null,
        Uri? cancelUrl = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        bool automaticTax = false) =>
        Create(customer, [OrderLine.Create(pricing, quantity)], CheckoutMode.Subscription,
            successUrl, cancelUrl, metadata, automaticTax);

    public static CheckoutOrder Create(
        Customer customer,
        IReadOnlyList<OrderLine> lines,
        CheckoutMode mode = CheckoutMode.Subscription,
        Uri? successUrl = null,
        Uri? cancelUrl = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        bool automaticTax = false)
    {
        if (lines.Count == 0)
            throw new DomainValidationException("the order must contain at least one line item");

        if (lines.Count > MaxLineCount)
            throw new DomainValidationException($"the order must not contain more than {MaxLineCount} line items");

        // A subscription needs something to subscribe to. Provider prices are
        // opaque here, so only reject when every line is a known one-time offer.
        if (mode == CheckoutMode.Subscription
            && lines.All(l => l.Pricing is AdHocOffer { IsRecurring: false }))
        {
            throw new DomainValidationException(
                "a subscription order requires at least one recurring line item");
        }

        // The inverse rule: a one-time payment cannot carry recurring offers,
        // which would otherwise be silently flattened into one-time charges.
        if (mode == CheckoutMode.Payment
            && lines.Any(l => l.Pricing is AdHocOffer { IsRecurring: true }))
        {
            throw new DomainValidationException("recurring offers only apply to subscription orders");
        }

        return new CheckoutOrder(customer, lines, mode, successUrl, cancelUrl, metadata, automaticTax);
    }
}

/// <summary>One thing being bought: a pricing and how many of it.</summary>
public sealed record OrderLine
{
    private OrderLine(Pricing pricing, long quantity)
    {
        Pricing = pricing;
        Quantity = quantity;
    }

    public Pricing Pricing { get; }
    public long Quantity { get; }

    /// <summary>Ceiling on a line's quantity; a typo'd quantity must fail as
    /// a 400 rather than multiply into a real charge.</summary>
    public const long MaxQuantity = 999;

    public static OrderLine Create(Pricing pricing, long? quantity = null)
    {
        if (quantity is < 1)
            throw new DomainValidationException("quantity must be at least 1");

        if (quantity > MaxQuantity)
            throw new DomainValidationException($"quantity must not exceed {MaxQuantity}");

        return new OrderLine(pricing, quantity ?? 1);
    }
}

public enum CheckoutMode
{
    /// <summary>Recurring billing.</summary>
    Subscription,

    /// <summary>One-time charge.</summary>
    Payment,
}
