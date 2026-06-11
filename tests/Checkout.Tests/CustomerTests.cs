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
    public void Rejects_missing_or_invalid_email(string? email)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Customer.Create(email));
        Assert.Contains("customer.email", ex.Message);
    }
}
