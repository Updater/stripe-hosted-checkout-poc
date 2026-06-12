using Checkout.Domain;

namespace Checkout.Tests;

public class CustomerTests
{
    [Fact]
    public void Creates_with_trimmed_email_and_optional_fields()
    {
        var customer = Customer.Create("  jane@example.com ", "Jane Doe", "+15555550123", "u-12345");

        Assert.Equal("jane@example.com", customer.Email);
        Assert.Equal("Jane Doe", customer.Name);
        Assert.Equal("+15555550123", customer.Phone);
        Assert.Equal("u-12345", customer.ExternalId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@")]
    [InlineData("a@")]
    [InlineData("@example.com")]
    [InlineData("jane doe@example.com")]
    [InlineData("Jane Doe <jane@example.com>")]
    [InlineData(@"miles.o'brien\@example.com")]
    public void Rejects_missing_or_invalid_email(string? email)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Customer.Create(email));
        Assert.Contains("customer.email", ex.Message);
    }

    [Theory]
    [InlineData("a@b")]
    [InlineData("miles.o'brien@example.com")]
    [InlineData("jane+promo@example.co.uk")]
    public void Accepts_unusual_but_valid_emails(string email) =>
        Assert.Equal(email, Customer.Create(email).Email);

    [Fact]
    public void Trims_the_provider_customer_id_and_treats_blank_as_absent()
    {
        Assert.Equal("cus_123", Customer.Create("jane@example.com", providerCustomerId: " cus_123 ").ProviderCustomerId);
        Assert.Null(Customer.Create("jane@example.com", providerCustomerId: "   ").ProviderCustomerId);
        Assert.Null(Customer.Create("jane@example.com").ProviderCustomerId);
    }
}
