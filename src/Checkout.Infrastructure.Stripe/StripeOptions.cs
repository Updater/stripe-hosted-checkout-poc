using System.ComponentModel.DataAnnotations;

namespace Checkout.Infrastructure.Stripe;

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>The merchant's Stripe secret key (sk_test_... / sk_live_...).</summary>
    [Required]
    public string SecretKey { get; init; } = "";

    /// <summary>Redirect after successful payment when the request doesn't
    /// specify one. Stripe substitutes the {CHECKOUT_SESSION_ID} placeholder.</summary>
    [Required]
    [RegularExpression(@"^https://.+", ErrorMessage = "DefaultSuccessUrl must be an https URL")]
    public string DefaultSuccessUrl { get; init; } = "";

    /// <summary>Redirect when the user abandons checkout and the request
    /// doesn't specify one.</summary>
    [Required]
    [RegularExpression(@"^https://.+", ErrorMessage = "DefaultCancelUrl must be an https URL")]
    public string DefaultCancelUrl { get; init; } = "";
}
