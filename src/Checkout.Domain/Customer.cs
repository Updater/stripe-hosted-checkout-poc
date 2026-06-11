namespace Checkout.Domain;

/// <summary>The customer being checked out.</summary>
public sealed record Customer
{
    private Customer(string email, string? name, string? phone, string? externalId)
    {
        Email = email;
        Name = name;
        Phone = phone;
        ExternalId = externalId;
    }

    public string Email { get; }
    public string? Name { get; }
    public string? Phone { get; }

    /// <summary>The calling system's identifier for this customer, used to
    /// correlate completed checkouts back to the caller.</summary>
    public string? ExternalId { get; }

    public static Customer Create(string? email, string? name = null, string? phone = null, string? externalId = null)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new DomainValidationException("customer.email is required and must be a valid email address");

        return new Customer(email.Trim(), name, phone, externalId);
    }
}
