using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AgentMail.AspNetCore;

/// <summary>
/// Tracks webhook deliveries by <c>svix-id</c>, which stays the same across retries of one event.
/// Replace the default in-memory implementation with a shared store (Redis, SQL, ...) when running
/// more than one instance.
/// </summary>
public interface IAgentMailWebhookDeduplicator
{
    /// <summary>Returns false if the delivery was already claimed and should not be processed again.</summary>
    ValueTask<bool> TryClaimAsync(string deliveryId, CancellationToken cancellationToken = default);

    /// <summary>Forgets a claim so a retry of a failed delivery is processed.</summary>
    ValueTask ReleaseAsync(string deliveryId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryAgentMailWebhookDeduplicator(
    IOptions<AgentMailWebhookOptions> options,
    TimeProvider timeProvider) : IAgentMailWebhookDeduplicator
{
    private const int PurgeThreshold = 10_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _claims = new(StringComparer.Ordinal);

    public ValueTask<bool> TryClaimAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now + options.Value.DeduplicationWindow;

        if (_claims.Count >= PurgeThreshold)
        {
            PurgeExpired(now);
        }

        while (true)
        {
            if (_claims.TryAdd(deliveryId, expiresAt))
            {
                return ValueTask.FromResult(true);
            }

            if (!_claims.TryGetValue(deliveryId, out var existing))
            {
                continue;
            }

            if (existing > now)
            {
                return ValueTask.FromResult(false);
            }

            // Expired claim: take it over, unless another request just did.
            if (_claims.TryUpdate(deliveryId, expiresAt, existing))
            {
                return ValueTask.FromResult(true);
            }
        }
    }

    public ValueTask ReleaseAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        _claims.TryRemove(deliveryId, out _);
        return ValueTask.CompletedTask;
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var (id, expiresAt) in _claims)
        {
            if (expiresAt <= now)
            {
                _claims.TryRemove(new KeyValuePair<string, DateTimeOffset>(id, expiresAt));
            }
        }
    }
}
