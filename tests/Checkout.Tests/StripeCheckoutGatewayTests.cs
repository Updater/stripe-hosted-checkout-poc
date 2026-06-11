using Checkout.Application;
using Checkout.Domain;
using Checkout.Infrastructure.Stripe;
using Stripe;
using Stripe.Checkout;
using DomainCustomer = Checkout.Domain.Customer;
using StripeCustomer = Stripe.Customer;

namespace Checkout.Tests;

/// <summary>
/// Tests the gateway's translation of domain orders into Stripe requests,
/// using stubbed Stripe services — no network or real key involved.
/// </summary>
public class StripeCheckoutGatewayTests
{
    private readonly StubCustomerService _customers = new();
    private readonly StubSessionService _sessions = new();
    private readonly StripeCheckoutGateway _gateway;

    public StripeCheckoutGatewayTests() =>
        _gateway = new StripeCheckoutGateway(
            new StripeOptions
            {
                SecretKey = "sk_test_placeholder",
                DefaultSuccessUrl = "https://merchant.example.com/success?session_id={CHECKOUT_SESSION_ID}",
                DefaultCancelUrl = "https://merchant.example.com/cancel",
            },
            _customers,
            _sessions);

    private static DomainCustomer Jane =>
        DomainCustomer.Create("jane@example.com", "Jane Doe", "+15555550123", "u-12345");

    private static Pricing Recurring(long? trialDays = null) =>
        Pricing.FromOffer("Residential 100 Mbps", 5500, "Up to 100 Mbps",
            schedule: new OfferSchedule(TrialDays: trialDays));

    private static Pricing OneTime() =>
        Pricing.FromOffer("Shipping & Handling", 2000, schedule: new OfferSchedule(Recurring: false));

    [Fact]
    public async Task Creates_a_customer_when_none_matches_the_email()
    {
        var order = CheckoutOrder.Create(Jane, Recurring());

        var session = await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal("jane@example.com", _customers.LastList!.Email);
        Assert.Equal("jane@example.com", _customers.LastCreate!.Email);
        Assert.Equal("Jane Doe", _customers.LastCreate.Name);
        Assert.Equal("+15555550123", _customers.LastCreate.Phone);
        Assert.Equal("u-12345", _customers.LastCreate.Metadata["external_id"]);
        Assert.Equal("cus_new_123", session.CustomerId);
        Assert.Equal("cus_new_123", _sessions.LastOptions!.Customer);
    }

    [Fact]
    public async Task Reuses_the_existing_customer_for_the_email()
    {
        _customers.Existing = new StripeCustomer { Id = "cus_existing_456" };
        var order = CheckoutOrder.Create(Jane, Recurring());

        var session = await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Null(_customers.LastCreate);
        Assert.Equal("cus_existing_456", session.CustomerId);
        Assert.Equal("cus_existing_456", _sessions.LastOptions!.Customer);
    }

    [Fact]
    public async Task Passes_special_character_emails_verbatim_through_the_structured_filter()
    {
        // The lookup is a structured list filter, not a search-query string, so
        // quotes and backslashes cannot inject extra clauses.
        var order = CheckoutOrder.Create(
            DomainCustomer.Create(@"miles.o'brien\@example.com"), Recurring());

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal(@"miles.o'brien\@example.com", _customers.LastList!.Email);
    }

    [Fact]
    public async Task Maps_a_provider_price_line()
    {
        var order = CheckoutOrder.Create(Jane, Pricing.FromPriceId("price_1ABC"), quantity: 2);

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        var line = Assert.Single(_sessions.LastOptions!.LineItems);
        Assert.Equal("price_1ABC", line.Price);
        Assert.Equal(2, line.Quantity);
        Assert.Null(line.PriceData);
        Assert.Equal("subscription", _sessions.LastOptions.Mode);
    }

    [Fact]
    public async Task Maps_a_recurring_offer_to_inline_price_data()
    {
        var order = CheckoutOrder.Create(Jane, Pricing.FromOffer(
            "Residential 100 Mbps", 5500, "Up to 100 Mbps", "EUR",
            new OfferSchedule(Interval: BillingInterval.Week, IntervalCount: 2)));

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        var priceData = Assert.Single(_sessions.LastOptions!.LineItems).PriceData;
        Assert.Equal("eur", priceData.Currency);
        Assert.Equal(5500, priceData.UnitAmount);
        Assert.Equal("Residential 100 Mbps", priceData.ProductData.Name);
        Assert.Equal("Up to 100 Mbps", priceData.ProductData.Description);
        Assert.Equal("week", priceData.Recurring.Interval);
        Assert.Equal(2, priceData.Recurring.IntervalCount);
    }

    [Fact]
    public async Task One_time_offer_rides_along_without_recurring_price_data()
    {
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(Recurring()), OrderLine.Create(OneTime())]);

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal(2, _sessions.LastOptions!.LineItems.Count);
        Assert.NotNull(_sessions.LastOptions.LineItems[0].PriceData.Recurring);
        Assert.Null(_sessions.LastOptions.LineItems[1].PriceData.Recurring);
    }

    [Fact]
    public async Task Payment_mode_never_emits_recurring_price_data_or_subscription_data()
    {
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(OneTime())], CheckoutMode.Payment);

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal("payment", _sessions.LastOptions!.Mode);
        Assert.Null(Assert.Single(_sessions.LastOptions.LineItems).PriceData.Recurring);
        Assert.Null(_sessions.LastOptions.SubscriptionData);
    }

    [Fact]
    public async Task Applies_the_longest_trial_across_recurring_lines()
    {
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(Recurring(trialDays: 7)), OrderLine.Create(Recurring(trialDays: 30))]);

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal(30, _sessions.LastOptions!.SubscriptionData!.TrialPeriodDays);
    }

    [Fact]
    public async Task Subscription_without_trials_sends_no_subscription_data()
    {
        var order = CheckoutOrder.Create(Jane, Recurring());

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Null(_sessions.LastOptions!.SubscriptionData);
    }

    [Fact]
    public async Task Automatic_tax_is_omitted_by_default()
    {
        var order = CheckoutOrder.Create(Jane, Recurring());

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Null(_sessions.LastOptions!.AutomaticTax);
    }

    [Fact]
    public async Task Automatic_tax_is_enabled_on_the_session_when_the_order_asks_for_it()
    {
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(Recurring())], automaticTax: true);

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.True(_sessions.LastOptions!.AutomaticTax!.Enabled);
    }

    [Fact]
    public async Task Restores_the_session_id_placeholder_in_an_explicit_success_url()
    {
        var order = CheckoutOrder.Create(Jane, Recurring(),
            successUrl: new Uri("https://partner.example.com/ok?session_id={CHECKOUT_SESSION_ID}"),
            cancelUrl: new Uri("https://partner.example.com/cancel"),
            metadata: new Dictionary<string, string> { ["campaign"] = "spring-promo" });

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal("https://partner.example.com/ok?session_id={CHECKOUT_SESSION_ID}", _sessions.LastOptions!.SuccessUrl);
        Assert.Equal("https://partner.example.com/cancel", _sessions.LastOptions.CancelUrl);
        Assert.Equal("u-12345", _sessions.LastOptions.ClientReferenceId);
        Assert.Equal("spring-promo", _sessions.LastOptions.Metadata["campaign"]);
    }

    [Fact]
    public async Task Falls_back_to_the_configured_default_urls()
    {
        var order = CheckoutOrder.Create(Jane, Recurring());

        await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal("https://merchant.example.com/success?session_id={CHECKOUT_SESSION_ID}", _sessions.LastOptions!.SuccessUrl);
        Assert.Equal("https://merchant.example.com/cancel", _sessions.LastOptions.CancelUrl);
    }

    [Fact]
    public async Task Returns_the_session_details_from_stripe()
    {
        var order = CheckoutOrder.Create(Jane, Recurring());

        var session = await _gateway.CreateSessionAsync(order, CancellationToken.None);

        Assert.Equal("cs_stub_123", session.SessionId);
        Assert.Equal("https://checkout.stripe.example/c/cs_stub_123", session.CheckoutUrl.OriginalString);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), session.ExpiresAtUtc);
    }

    [Fact]
    public async Task Wraps_stripe_errors_in_a_gateway_exception()
    {
        _sessions.OnCreate = () => throw new StripeException("http transport detail")
        {
            StripeError = new StripeError { Message = "Your card was declined." },
        };
        var order = CheckoutOrder.Create(Jane, Recurring());

        var ex = await Assert.ThrowsAsync<CheckoutGatewayException>(
            () => _gateway.CreateSessionAsync(order, CancellationToken.None));

        Assert.Equal("Your card was declined.", ex.Message);
        Assert.IsType<StripeException>(ex.InnerException);
    }

    [Fact]
    public async Task Falls_back_to_the_exception_message_when_stripe_sends_no_error_body()
    {
        _sessions.OnCreate = () => throw new StripeException("connection reset");
        var order = CheckoutOrder.Create(Jane, Recurring());

        var ex = await Assert.ThrowsAsync<CheckoutGatewayException>(
            () => _gateway.CreateSessionAsync(order, CancellationToken.None));

        Assert.Equal("connection reset", ex.Message);
    }

    private sealed class StubCustomerService : CustomerService
    {
        public CustomerListOptions? LastList { get; private set; }
        public CustomerCreateOptions? LastCreate { get; private set; }
        public StripeCustomer? Existing { get; set; }

        public override Task<StripeList<StripeCustomer>> ListAsync(
            CustomerListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastList = options;
            return Task.FromResult(new StripeList<StripeCustomer>
            {
                Data = Existing is null ? [] : [Existing],
            });
        }

        public override Task<StripeCustomer> CreateAsync(
            CustomerCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastCreate = options;
            return Task.FromResult(new StripeCustomer { Id = "cus_new_123" });
        }
    }

    private sealed class StubSessionService : SessionService
    {
        public SessionCreateOptions? LastOptions { get; private set; }
        public Func<Session>? OnCreate { get; set; }

        public override Task<Session> CreateAsync(
            SessionCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(OnCreate?.Invoke() ?? new Session
            {
                Id = "cs_stub_123",
                Url = "https://checkout.stripe.example/c/cs_stub_123",
                ExpiresAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            });
        }
    }
}
