using Checkout.Application;
using Checkout.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Checkout.Infrastructure.Stripe;

/// <summary>
/// Stripe adapter for <see cref="ICheckoutGateway"/>: finds-or-creates the
/// Stripe Customer and creates a Hosted Checkout session.
/// </summary>
public sealed class StripeCheckoutGateway : ICheckoutGateway
{
    private readonly CustomerService _customers;
    private readonly SessionService _sessions;
    private readonly StripeOptions _options;
    private readonly ILogger _logger;

    public StripeCheckoutGateway(IOptions<StripeOptions> options, ILogger<StripeCheckoutGateway> logger)
        : this(options.Value, new StripeClient(options.Value.SecretKey), logger) { }

    private StripeCheckoutGateway(StripeOptions options, StripeClient client, ILogger logger)
        : this(options, new CustomerService(client), new SessionService(client), logger) { }

    /// <summary>Test seam: lets tests substitute stub Stripe services.</summary>
    internal StripeCheckoutGateway(
        StripeOptions options, CustomerService customers, SessionService sessions, ILogger? logger = null)
    {
        _options = options;
        _customers = customers;
        _sessions = sessions;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<CheckoutSession> CreateSessionAsync(
        CheckoutOrder order, string? idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = await ResolveCustomerIdAsync(order.Customer, idempotencyKey, cancellationToken);

            var session = await _sessions.CreateAsync(new SessionCreateOptions
            {
                Mode = order.Mode == CheckoutMode.Subscription ? "subscription" : "payment",
                Customer = customerId,
                LineItems = [.. order.Lines.Select(ToLineItem)],
                SubscriptionData = ToSubscriptionData(order),
                SuccessUrl = (order.SuccessUrl?.ToString() ?? _options.DefaultSuccessUrl)
                    // Stripe substitutes this placeholder; Uri escapes the braces.
                    .Replace("%7BCHECKOUT_SESSION_ID%7D", "{CHECKOUT_SESSION_ID}"),
                CancelUrl = order.CancelUrl?.ToString() ?? _options.DefaultCancelUrl,
                ClientReferenceId = order.Customer.ExternalId,
                Metadata = order.Metadata?.ToDictionary(),
                // Needs Stripe Tax enabled on the merchant account — otherwise
                // Stripe rejects the session, surfaced as a 502.
                AutomaticTax = order.AutomaticTax
                    ? new SessionAutomaticTaxOptions { Enabled = true }
                    : null,
            }, IdempotentRequest(idempotencyKey, "session"), cancellationToken);

            return new CheckoutSession(session.Id, new Uri(session.Url), customerId, session.ExpiresAt);
        }
        catch (StripeException e) when (e.StripeError?.Param?.StartsWith("automatic_tax", StringComparison.Ordinal) is true)
        {
            // A merchant-account misconfiguration the caller opted into, not a
            // provider outage — surface it as a 400 with the actual cause. The
            // 400 path is not logged by the endpoint, so record the full
            // Stripe failure here; operators must not depend on caller reports
            // to learn Stripe Tax is broken.
            Log.AutomaticTaxNotConfigured(_logger, e);
            throw new DomainValidationException(
                "automaticTax requires Stripe Tax to be enabled on the merchant account"
                + $" ({e.StripeError.Message})", e);
        }
        catch (StripeException e)
        {
            throw new CheckoutGatewayException(e.StripeError?.Message ?? e.Message, e);
        }
    }

    /// <summary>
    /// Distinct sub-keys per write: Stripe scopes idempotency keys to a single
    /// request shape, so reusing the caller's key verbatim on both the
    /// customer create and the session create would make the second call fail
    /// as a parameter mismatch.
    /// </summary>
    private static RequestOptions? IdempotentRequest(string? idempotencyKey, string operation) =>
        idempotencyKey is null ? null : new RequestOptions { IdempotencyKey = $"{idempotencyKey}:{operation}" };

    /// <summary>
    /// A caller-supplied provider customer ID wins outright: it is the
    /// canonical mapping, needs no lookup, and is immune to the
    /// duplicate-customer race below. Otherwise, reuses the Stripe Customer if
    /// one already exists for this email so repeat checkouts and subscriptions
    /// attach to a single customer record. Uses the list endpoint's
    /// exact-match email filter rather than Customer Search: the search index
    /// is eventually consistent, so a just-created customer would not be found
    /// and repeat checkouts would duplicate it. Two concurrent first-time
    /// checkouts for the same email can still both pass the lookup and create
    /// duplicates — callers who care should send providerCustomerId.
    /// </summary>
    private async Task<string> ResolveCustomerIdAsync(
        Domain.Customer customer, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (customer.ProviderCustomerId is not null)
            return customer.ProviderCustomerId;

        var existing = await _customers.ListAsync(new CustomerListOptions
        {
            Email = customer.Email,
            Limit = 1,
        }, cancellationToken: cancellationToken);

        if (existing.Data.FirstOrDefault() is { } found)
            return found.Id;

        var created = await _customers.CreateAsync(new CustomerCreateOptions
        {
            Email = customer.Email,
            Name = customer.Name,
            Phone = customer.Phone,
            Metadata = customer.ExternalId is null
                ? null
                : new Dictionary<string, string> { ["external_id"] = customer.ExternalId },
        }, IdempotentRequest(idempotencyKey, "customer"), cancellationToken);

        return created.Id;
    }

    private static SessionLineItemOptions ToLineItem(OrderLine line) => line.Pricing switch
    {
        ProviderPrice p => new SessionLineItemOptions
        {
            Price = p.PriceId,
            Quantity = line.Quantity,
        },
        AdHocOffer o => new SessionLineItemOptions
        {
            Quantity = line.Quantity,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = o.Currency,
                UnitAmount = o.AmountCents,
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = o.Name,
                    Description = o.Description,
                },
                // One-time offers (Interval == null) ride along on the first
                // invoice when the session is in subscription mode.
                Recurring = o.Interval is not null
                    ? new SessionLineItemPriceDataRecurringOptions
                    {
                        Interval = o.Interval.ToString()!.ToLowerInvariant(),
                        IntervalCount = o.IntervalCount,
                    }
                    : null,
            },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(line), $"unsupported pricing type {line.Pricing.GetType().Name}"),
    };

    private static SessionSubscriptionDataOptions? ToSubscriptionData(CheckoutOrder order)
    {
        if (order.Mode != CheckoutMode.Subscription)
            return null;

        var trial = order.Lines
            .Select(l => l.Pricing)
            .OfType<AdHocOffer>()
            .Where(o => o.IsRecurring)
            .Max(o => o.TrialDays);

        return trial is > 0 ? new SessionSubscriptionDataOptions { TrialPeriodDays = trial } : null;
    }
}
