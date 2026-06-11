using Checkout.Application;
using Checkout.Domain;
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

    public StripeCheckoutGateway(IOptions<StripeOptions> options)
        : this(options.Value, new StripeClient(options.Value.SecretKey)) { }

    private StripeCheckoutGateway(StripeOptions options, StripeClient client)
        : this(options, new CustomerService(client), new SessionService(client)) { }

    /// <summary>Test seam: lets tests substitute stub Stripe services.</summary>
    internal StripeCheckoutGateway(StripeOptions options, CustomerService customers, SessionService sessions)
    {
        _options = options;
        _customers = customers;
        _sessions = sessions;
    }

    public async Task<CheckoutSession> CreateSessionAsync(CheckoutOrder order, CancellationToken cancellationToken)
    {
        try
        {
            var customer = await FindOrCreateCustomerAsync(order.Customer, cancellationToken);

            var session = await _sessions.CreateAsync(new SessionCreateOptions
            {
                Mode = order.Mode == CheckoutMode.Subscription ? "subscription" : "payment",
                Customer = customer.Id,
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
            }, cancellationToken: cancellationToken);

            return new CheckoutSession(session.Id, new Uri(session.Url), customer.Id, session.ExpiresAt);
        }
        catch (StripeException e)
        {
            throw new CheckoutGatewayException(e.StripeError?.Message ?? e.Message, e);
        }
    }

    /// <summary>
    /// Reuses the Stripe Customer if one already exists for this email so
    /// repeat checkouts and subscriptions attach to a single customer record.
    /// Uses the list endpoint's exact-match email filter rather than Customer
    /// Search: the search index is eventually consistent, so a just-created
    /// customer would not be found and repeat checkouts would duplicate it.
    /// </summary>
    private async Task<global::Stripe.Customer> FindOrCreateCustomerAsync(
        Domain.Customer customer, CancellationToken cancellationToken)
    {
        var existing = await _customers.ListAsync(new CustomerListOptions
        {
            Email = customer.Email,
            Limit = 1,
        }, cancellationToken: cancellationToken);

        return existing.Data.FirstOrDefault() ?? await _customers.CreateAsync(new CustomerCreateOptions
        {
            Email = customer.Email,
            Name = customer.Name,
            Phone = customer.Phone,
            Metadata = customer.ExternalId is null
                ? null
                : new Dictionary<string, string> { ["external_id"] = customer.ExternalId },
        }, cancellationToken: cancellationToken);
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
