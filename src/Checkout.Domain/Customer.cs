namespace Checkout.Domain;

/// <summary>The customer being checked out.</summary>
public sealed record Customer
{
    private Customer(string email, string? name, string? phone, string? externalId, string? providerCustomerId)
    {
        Email = email;
        Name = name;
        Phone = phone;
        ExternalId = externalId;
        ProviderCustomerId = providerCustomerId;
    }

    public string Email { get; }
    public string? Name { get; }
    public string? Phone { get; }

    /// <summary>The calling system's identifier for this customer, used to
    /// correlate completed checkouts back to the caller.</summary>
    public string? ExternalId { get; }

    /// <summary>The payment provider's identifier for this customer (e.g. a
    /// Stripe Customer ID). When the caller maintains its own canonical
    /// customer mapping, supplying this skips the find-or-create-by-email
    /// lookup — and with it the duplicate-customer race between concurrent
    /// first-time checkouts.</summary>
    public string? ProviderCustomerId { get; }

    public static Customer Create(
        string? email, string? name = null, string? phone = null,
        string? externalId = null, string? providerCustomerId = null)
    {
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email.Trim()))
            throw new DomainValidationException("customer.email is required and must be a valid email address");

        return new Customer(email.Trim(), name, phone, externalId,
            string.IsNullOrWhiteSpace(providerCustomerId) ? null : providerCustomerId.Trim());
    }

    /// <summary>
    /// Catches obvious non-emails ("@", "a@", "jane at example") here as a 400
    /// instead of letting the provider reject them as a 502. Full RFC
    /// compliance is intentionally not the goal — the provider has the final
    /// say. The round-trip comparison rejects forms MailAddress would silently
    /// reinterpret, like display names ("Jane &lt;jane@example.com&gt;").
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        try
        {
            return new System.Net.Mail.MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
