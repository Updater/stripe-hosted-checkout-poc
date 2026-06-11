namespace Checkout.Domain;

/// <summary>Thrown when input violates a domain invariant.</summary>
public sealed class DomainValidationException(string message) : Exception(message);
