using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        return endpoints.MapPost(pattern, async (
            HttpContext context,
            IOptions<AgentMailOptions> options,
            CancellationToken cancellationToken) =>
        {
            var secret = options.Value.WebhookSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return Results.Problem(
                    "AgentMail webhook verification is not configured.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);

            var rawBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            context.Request.Body.Position = 0;

            if (!AgentMailWebhookVerifier.Verify(
                    secret,
                    context.Request.Headers["svix-id"].ToString(),
                    context.Request.Headers["svix-timestamp"].ToString(),
                    context.Request.Headers["svix-signature"].ToString(),
                    rawBody,
                    options.Value.WebhookTimestampTolerance))
            {
                return Results.Unauthorized();
            }

            AgentMailWebhookEvent? webhookEvent;
            try
            {
                webhookEvent = JsonSerializer.Deserialize<AgentMailWebhookEvent>(rawBody, JsonOptions);
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }

            if (webhookEvent is null)
            {
                return Results.BadRequest();
            }

            await handler(webhookEvent, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
    }
}
