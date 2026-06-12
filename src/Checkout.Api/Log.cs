namespace Checkout.Api;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Checkout gateway failure creating session")]
    public static partial void CheckoutGatewayFailure(ILogger logger, Exception e);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected failure creating session")]
    public static partial void UnexpectedFailure(ILogger logger, Exception e);
}
