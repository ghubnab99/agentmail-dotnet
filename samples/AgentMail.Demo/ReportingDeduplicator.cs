using AgentMail.AspNetCore;

namespace AgentMail.Demo;

/// <summary>
/// Wraps the SDK's in-memory deduplicator so the dashboard can show duplicates it actually blocked.
/// The endpoint calls this only after the delivery's signature was verified and its payload parsed.
/// </summary>
public sealed class ReportingDeduplicator(InMemoryAgentMailWebhookDeduplicator inner, DemoState state)
    : IAgentMailWebhookDeduplicator
{
    public async ValueTask<bool> TryClaimAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        if (await inner.TryClaimAsync(deliveryId, cancellationToken))
        {
            return true;
        }

        var existing = state.FindByDelivery(deliveryId);
        if (existing is null)
        {
            state.Log("warn", "Duplicate delivery detected", "Already processed; handler not run again");
        }
        else
        {
            state.Update(() => existing.DuplicatesBlocked++);
            state.Log("warn", "Duplicate delivery detected",
                $"Existing case {existing.Id} · no second case created · no second customer reply sent", existing.Id);
        }

        return false;
    }

    public ValueTask ReleaseAsync(string deliveryId, CancellationToken cancellationToken = default) =>
        inner.ReleaseAsync(deliveryId, cancellationToken);
}
