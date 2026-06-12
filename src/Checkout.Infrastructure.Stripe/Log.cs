using Microsoft.Extensions.Logging;

namespace Checkout.Infrastructure.Stripe;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Error,
        Message = "Stripe rejected automaticTax: Stripe Tax is not configured on the merchant account")]
    public static partial void AutomaticTaxNotConfigured(ILogger logger, Exception e);
}
