using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentMail.AspNetCore;

public static class EndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointConventionBuilder MapAgentMailWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<AgentMailWebhookEvent, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return endpoints.MapAgentMailWebhook(pattern, (evt, _, ct) => handler(evt, ct));
    }

    /// <summary>
    /// Maps a POST endpoint that verifies the Svix signature, drops duplicate deliveries and invokes
    /// <paramref name="handler"/> with the typed event and the request's service provider.
    /// Requires <see cref="ServiceCollectionExtensions.AddAgentMailWebhooks"/>.
    /// </summary>
    public static IEndpointConventionBuilder MapAgentMailWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<AgentMailWebhookEvent, IServiceProvider, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        return endpoints.MapPost(pattern, async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var services = context.RequestServices;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentMail.Webhooks");
            var deduplicator = services.GetService<IAgentMailWebhookDeduplicator>();
            if (deduplicator is null)
            {
                logger.LogError("AgentMail webhook received but AddAgentMailWebhooks() was not called.");
                return Results.Problem(
                    "AgentMail webhook verification is not configured.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var options = services.GetRequiredService<IOptions<AgentMailWebhookOptions>>().Value;
            var timeProvider = services.GetRequiredService<TimeProvider>();

            // Svix signs the exact bytes, so verify before any parsing.
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var rawBody = buffer.ToArray();

            var deliveryId = Header(context, "svix-id", "webhook-id");
            if (!AgentMailWebhookVerifier.Verify(
                    options.Secret,
                    deliveryId,
                    Header(context, "svix-timestamp", "webhook-timestamp"),
                    Header(context, "svix-signature", "webhook-signature"),
                    rawBody,
                    options.TimestampTolerance,
                    timeProvider))
            {
                logger.LogWarning("Rejected AgentMail webhook {DeliveryId}: invalid signature or timestamp.", deliveryId);
                return Results.BadRequest();
            }

            AgentMailWebhookEvent? webhookEvent;
            try
            {
                webhookEvent = JsonSerializer.Deserialize<AgentMailWebhookEvent>(rawBody, JsonOptions);
            }
            catch (JsonException)
            {
                webhookEvent = null;
            }

            if (webhookEvent is null)
            {
                logger.LogWarning("Rejected AgentMail webhook {DeliveryId}: payload is not a valid event.", deliveryId);
                return Results.BadRequest();
            }

            if (!await deduplicator.TryClaimAsync(deliveryId, cancellationToken).ConfigureAwait(false))
            {
                logger.LogInformation(
                    "Ignored duplicate AgentMail webhook {DeliveryId} ({EventType}).", deliveryId, webhookEvent.EventType);
                return Results.NoContent();
            }

            try
            {
                await handler(webhookEvent, services, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Let the sender's retry reach the handler again.
                await deduplicator.ReleaseAsync(deliveryId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            return Results.NoContent();
        });
    }

    // Svix also sends the unbranded "webhook-*" header names.
    private static string Header(HttpContext context, string name, string fallback)
    {
        var value = context.Request.Headers[name].ToString();
        return string.IsNullOrEmpty(value) ? context.Request.Headers[fallback].ToString() : value;
    }
}
