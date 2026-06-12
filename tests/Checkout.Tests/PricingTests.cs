using Checkout.Domain;

namespace Checkout.Tests;

public class PricingTests
{
    [Fact]
    public void Offer_defaults_to_monthly_usd_subscription()
    {
        var offer = Assert.IsType<AdHocOffer>(Pricing.FromOffer("Residential Internet", 12000));

        Assert.Equal("usd", offer.Currency);
        Assert.Equal(BillingInterval.Month, offer.Interval);
        Assert.Equal(1, offer.IntervalCount);
        Assert.Null(offer.TrialDays);
        Assert.True(offer.IsRecurring);
    }

    [Fact]
    public void Offer_lowercases_currency()
    {
        var offer = Assert.IsType<AdHocOffer>(Pricing.FromOffer("Plan", 1000, currency: "EUR"));

        Assert.Equal("eur", offer.Currency);
    }

    [Fact]
    public void One_time_offer_has_no_interval()
    {
        var offer = Assert.IsType<AdHocOffer>(
            Pricing.FromOffer("Shipping & Handling", 2000, schedule: new OfferSchedule(Recurring: false)));

        Assert.False(offer.IsRecurring);
        Assert.Null(offer.Interval);
        Assert.Null(offer.IntervalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Offer_requires_name(string? name)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Pricing.FromOffer(name, 1000));
        Assert.Contains("offer.name", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void Offer_requires_positive_amount(long amountCents)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Pricing.FromOffer("Plan", amountCents));
        Assert.Contains("amountCents", ex.Message);
    }

    [Fact]
    public void Offer_rejects_amount_above_the_ceiling()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => Pricing.FromOffer("Plan", Pricing.MaxAmountCents + 1));
        Assert.Contains("must not exceed", ex.Message);

        Assert.IsType<AdHocOffer>(Pricing.FromOffer("Plan", Pricing.MaxAmountCents));
    }

    [Fact]
    public void Offer_rejects_intervalCount_below_one()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => Pricing.FromOffer("Plan", 1000, schedule: new OfferSchedule(IntervalCount: 0)));
        Assert.Contains("intervalCount", ex.Message);
    }

    [Fact]
    public void Offer_rejects_trialDays_below_one()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => Pricing.FromOffer("Plan", 1000, schedule: new OfferSchedule(TrialDays: 0)));
        Assert.Contains("trialDays", ex.Message);
    }

    [Fact]
    public void One_time_offer_rejects_recurring_only_fields()
    {
        var ex = Assert.Throws<DomainValidationException>(
            () => Pricing.FromOffer("Shipping", 2000, schedule: new OfferSchedule(Recurring: false, TrialDays: 7)));
        Assert.Contains("only apply to recurring offers", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PriceId_must_be_non_empty(string? priceId)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Pricing.FromPriceId(priceId));
        Assert.Contains("priceId", ex.Message);
    }
}
