using Checkout.Application;
using Checkout.Domain;

namespace Checkout.Tests;

public class CreateCheckoutSessionTests
{
    private readonly FakeCheckoutGateway _gateway = new();
    private readonly CreateCheckoutSession _useCase;

    public CreateCheckoutSessionTests() => _useCase = new CreateCheckoutSession(_gateway);

    private static CreateCheckoutSessionCommand Command(
        CustomerDto? customer = null,
        string? priceId = null,
        OfferDto? offer = null,
        List<LineItemDto>? lineItems = null,
        long? quantity = null,
        string? mode = null,
        string? successUrl = null,
        string? cancelUrl = null,
        Dictionary<string, string>? metadata = null,
        bool? automaticTax = null) =>
        new(customer ?? new CustomerDto("jane@example.com", null, null, null),
            priceId, offer, lineItems, quantity, mode, successUrl, cancelUrl, metadata, automaticTax);

    private static OfferDto Offer(
        string name = "Residential Internet",
        long amountCents = 12000,
        string? interval = null,
        long? trialDays = null,
        bool? recurring = null) =>
        new(name, null, amountCents, null, interval, null, trialDays, recurring);

    [Fact]
    public async Task Creates_session_for_a_single_inline_offer()
    {
        var session = await _useCase.ExecuteAsync(Command(offer: Offer()));

        Assert.Equal("cs_fake_123", session.SessionId);
        var line = Assert.Single(_gateway.LastOrder!.Lines);
        var offer = Assert.IsType<AdHocOffer>(line.Pricing);
        Assert.Equal("Residential Internet", offer.Name);
        Assert.Equal(12000, offer.AmountCents);
    }

    [Fact]
    public async Task Creates_session_for_a_provider_price()
    {
        await _useCase.ExecuteAsync(Command(priceId: "price_1ABC", quantity: 2));

        var line = Assert.Single(_gateway.LastOrder!.Lines);
        Assert.Equal(new ProviderPrice("price_1ABC"), line.Pricing);
        Assert.Equal(2, line.Quantity);
    }

    [Fact]
    public async Task Creates_session_for_a_bundle_of_line_items()
    {
        await _useCase.ExecuteAsync(Command(lineItems:
        [
            new LineItemDto(null, Offer("Residential 100 Mbps", 5500, interval: "month"), null),
            new LineItemDto(null, Offer("Hardware Rental", 1000, interval: "month"), null),
            new LineItemDto(null, Offer("Shipping & Handling", 2000, recurring: false), 1),
        ]));

        var order = _gateway.LastOrder!;
        Assert.Equal(3, order.Lines.Count);
        Assert.Equal(
            [true, true, false],
            order.Lines.Select(l => ((AdHocOffer)l.Pricing).IsRecurring));
    }

    [Fact]
    public async Task Rejects_command_with_none_of_priceId_offer_or_lineItems()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command()));
        Assert.Contains("exactly one of priceId, offer or lineItems", ex.Message);
    }

    [Fact]
    public async Task Rejects_command_with_more_than_one_pricing_shape()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command(priceId: "price_1ABC", offer: Offer())));
        Assert.Contains("exactly one of priceId, offer or lineItems", ex.Message);
    }

    [Fact]
    public async Task Rejects_top_level_quantity_with_lineItems()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _useCase.ExecuteAsync(
            Command(lineItems: [new LineItemDto("price_1ABC", null, null)], quantity: 2)));
        Assert.Contains("quantity belongs on each line item", ex.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Rejects_line_item_without_exactly_one_pricing(bool withPriceId, bool withOffer)
    {
        var item = new LineItemDto(withPriceId ? "price_1ABC" : null, withOffer ? Offer() : null, null);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command(lineItems: [item])));
        Assert.Contains("each line item requires exactly one of priceId or offer", ex.Message);
    }

    [Fact]
    public async Task Mode_defaults_to_subscription_and_parses_payment()
    {
        await _useCase.ExecuteAsync(Command(offer: Offer()));
        Assert.Equal(CheckoutMode.Subscription, _gateway.LastOrder!.Mode);

        await _useCase.ExecuteAsync(Command(offer: Offer(recurring: false), mode: "payment"));
        Assert.Equal(CheckoutMode.Payment, _gateway.LastOrder!.Mode);
    }

    [Fact]
    public async Task Payment_mode_defaults_a_plain_offer_to_one_time()
    {
        await _useCase.ExecuteAsync(Command(offer: Offer(), mode: "payment"));

        var offer = Assert.IsType<AdHocOffer>(Assert.Single(_gateway.LastOrder!.Lines).Pricing);
        Assert.False(offer.IsRecurring);
    }

    [Fact]
    public async Task Payment_mode_rejects_an_offer_with_an_interval()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _useCase.ExecuteAsync(
            Command(offer: Offer(interval: "month"), mode: "payment")));
        Assert.Contains("only apply to recurring offers", ex.Message);
    }

    [Fact]
    public async Task Payment_mode_rejects_an_explicitly_recurring_offer()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _useCase.ExecuteAsync(
            Command(offer: Offer(recurring: true), mode: "payment")));
        Assert.Contains("recurring offers only apply to subscription orders", ex.Message);
    }

    [Fact]
    public async Task Automatic_tax_defaults_off_and_passes_through_when_requested()
    {
        await _useCase.ExecuteAsync(Command(offer: Offer()));
        Assert.False(_gateway.LastOrder!.AutomaticTax);

        await _useCase.ExecuteAsync(Command(offer: Offer(), automaticTax: true));
        Assert.True(_gateway.LastOrder!.AutomaticTax);
    }

    [Fact]
    public async Task Rejects_unknown_mode()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command(offer: Offer(), mode: "setup")));
        Assert.Contains("unknown mode 'setup'", ex.Message);
    }

    [Fact]
    public async Task Rejects_unknown_interval()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command(offer: Offer(interval: "fortnight"))));
        Assert.Contains("unknown interval 'fortnight'", ex.Message);
    }

    [Theory]
    [InlineData("http://partner.example.com/ok")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    public async Task Rejects_non_https_success_url(string url)
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command(offer: Offer(), successUrl: url)));
        Assert.Equal("successUrl must be an absolute https URL", ex.Message);
    }

    [Fact]
    public async Task Rejects_non_https_cancel_url()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _useCase.ExecuteAsync(Command(offer: Offer(), cancelUrl: "http://partner.example.com/cancel")));
        Assert.Equal("cancelUrl must be an absolute https URL", ex.Message);
    }

    [Fact]
    public async Task Passes_urls_customer_and_metadata_through_to_the_order()
    {
        await _useCase.ExecuteAsync(Command(
            customer: new CustomerDto("jane@example.com", "Jane Doe", "+15555550123", "u-12345"),
            offer: Offer(),
            successUrl: "https://partner.example.com/ok?session_id={CHECKOUT_SESSION_ID}",
            cancelUrl: "https://partner.example.com/cancel",
            metadata: new Dictionary<string, string> { ["campaign"] = "spring-promo" }));

        var order = _gateway.LastOrder!;
        Assert.Equal("u-12345", order.Customer.ExternalId);
        Assert.Equal("https://partner.example.com/ok?session_id={CHECKOUT_SESSION_ID}", order.SuccessUrl!.OriginalString);
        Assert.Equal("https://partner.example.com/cancel", order.CancelUrl!.OriginalString);
        Assert.Equal("spring-promo", order.Metadata!["campaign"]);
    }

    [Fact]
    public async Task Rejects_missing_customer()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _useCase.ExecuteAsync(
            new CreateCheckoutSessionCommand(null, null, Offer(), null, null, null, null, null, null)));
        Assert.Contains("customer.email", ex.Message);
    }
}

/// <summary>Records the order handed to the gateway and returns a canned session.</summary>
internal sealed class FakeCheckoutGateway : ICheckoutGateway
{
    public CheckoutOrder? LastOrder { get; private set; }

    public Task<CheckoutSession> CreateSessionAsync(CheckoutOrder order, CancellationToken cancellationToken)
    {
        LastOrder = order;
        return Task.FromResult(new CheckoutSession(
            "cs_fake_123",
            new Uri("https://checkout.example.com/c/pay/cs_fake_123"),
            "cus_fake_123",
            DateTime.UtcNow.AddDays(1)));
    }
}
