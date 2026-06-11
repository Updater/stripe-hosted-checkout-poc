using Checkout.Api;
using Checkout.Application;
using Checkout.Domain;
using Checkout.Infrastructure.Stripe;
using Microsoft.AspNetCore.HttpLogging;

var builder = WebApplication.CreateBuilder(args);

// One line per request (method, path, status, duration); headers and bodies are
// excluded so the X-Api-Key secret never reaches the logs. Emitted at Information,
// so production stays quiet unless its log level opts in.
builder.Services.AddHttpLogging(logging => logging.LoggingFields =
    HttpLoggingFields.RequestMethod
    | HttpLoggingFields.RequestPath
    | HttpLoggingFields.ResponseStatusCode
    | HttpLoggingFields.Duration);

builder.Services.AddStripeCheckout(builder.Configuration);
builder.Services.AddScoped<CreateCheckoutSession>();

builder.Services.AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

var app = builder.Build();

app.UseHttpLogging();

// HTTPS-only, including development; HSTS instructs clients to never retry http.
app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

var createCheckoutSession =
    async (CreateCheckoutSessionCommand command, CreateCheckoutSession useCase,
        ILogger<CreateCheckoutSession> logger, CancellationToken ct) =>
    {
        try
        {
            var session = await useCase.ExecuteAsync(command, ct);
            return Results.Ok(new
            {
                sessionId = session.SessionId,
                checkoutUrl = session.CheckoutUrl,
                customerId = session.CustomerId,
                expiresAt = session.ExpiresAtUtc,
            });
        }
        catch (DomainValidationException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
        catch (CheckoutGatewayException e)
        {
            // The response body only carries the message; keep the full
            // exception (Stripe error code, request id) in the server log.
            Log.CheckoutGatewayFailure(logger, e);
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    };

app.MapPost("/api/checkout-sessions", createCheckoutSession)
    .AddEndpointFilter<ApiKeyEndpointFilter>();

if (app.Environment.IsDevelopment())
{
    // Demo front end (wwwroot), Development only. The page never sees the API
    // key: it posts to this same-origin demo endpoint, which stands in for the
    // partner backend that would hold the key — so the key stays server-side.
    // Neither the pages nor the endpoint exist outside Development.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapPost("/demo/checkout-sessions", createCheckoutSession);
}

await app.RunAsync();
