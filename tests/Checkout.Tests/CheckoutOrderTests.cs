using Checkout.Domain;

namespace Checkout.Tests;

public class CheckoutOrderTests
{
    private static readonly Customer Jane = Customer.Create("jane@example.com");

    private static Pricing Recurring(long? trialDays = null) =>
        Pricing.FromOffer("Residential 100 Mbps", 5500, schedule: new OfferSchedule(TrialDays: trialDays));

    private static Pricing OneTime() =>
        Pricing.FromOffer("Shipping & Handling", 2000, schedule: new OfferSchedule(Recurring: false));

    [Fact]
    public void Order_requires_at_least_one_line()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => CheckoutOrder.Create(Jane, []));
        Assert.Contains("at least one line item", ex.Message);
    }

    [Fact]
    public void Subscription_order_rejects_all_one_time_lines()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => CheckoutOrder.Create(Jane, [OrderLine.Create(OneTime())]));
        Assert.Contains("at least one recurring line item", ex.Message);
    }

    [Fact]
    public void Subscription_order_allows_one_time_lines_alongside_a_recurring_one()
    {
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(Recurring()), OrderLine.Create(OneTime())]);

        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(CheckoutMode.Subscription, order.Mode);
    }

    [Fact]
    public void Subscription_order_trusts_opaque_provider_prices()
    {
        // A provider price might be recurring; the domain can't tell, so it passes.
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(Pricing.FromPriceId("price_123"))]);

        Assert.Single(order.Lines);
    }

    [Fact]
    public void Payment_order_rejects_recurring_lines()
    {
        var ex = Assert.Throws<DomainValidationException>(() => CheckoutOrder.Create(
            Jane, [OrderLine.Create(Recurring())], CheckoutMode.Payment));
        Assert.Contains("recurring offers only apply to subscription orders", ex.Message);
    }

    [Fact]
    public void Payment_order_allows_one_time_lines()
    {
        var order = CheckoutOrder.Create(
            Jane, [OrderLine.Create(OneTime())], CheckoutMode.Payment);

        Assert.Equal(CheckoutMode.Payment, order.Mode);
    }

    [Fact]
    public void Line_quantity_defaults_to_one() =>
        Assert.Equal(1, OrderLine.Create(Recurring()).Quantity);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_rejects_quantity_below_one(long quantity)
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => OrderLine.Create(Recurring(), quantity));
        Assert.Contains("quantity must be at least 1", ex.Message);
    }

    [Fact]
    public void Line_rejects_quantity_above_the_ceiling()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => OrderLine.Create(Recurring(), OrderLine.MaxQuantity + 1));
        Assert.Contains("must not exceed", ex.Message);

        Assert.Equal(OrderLine.MaxQuantity, OrderLine.Create(Recurring(), OrderLine.MaxQuantity).Quantity);
    }

    [Fact]
    public void Order_rejects_more_lines_than_the_ceiling()
    {
        var tooMany = Enumerable.Range(0, CheckoutOrder.MaxLineCount + 1)
            .Select(_ => OrderLine.Create(Recurring()))
            .ToList();

        var ex = Assert.Throws<DomainValidationException>(
            () => CheckoutOrder.Create(Jane, tooMany));
        Assert.Contains($"more than {CheckoutOrder.MaxLineCount} line items", ex.Message);
    }
}
