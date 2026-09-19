using Microsoft.Extensions.Options;

namespace AgentMail.AspNetCore.Tests;

public sealed class InMemoryAgentMailWebhookDeduplicatorTests
{
    private readonly ManualTimeProvider _time = new(Svix.SignedAt);
    private readonly InMemoryAgentMailWebhookDeduplicator _deduplicator;

    public InMemoryAgentMailWebhookDeduplicatorTests()
    {
        var options = Options.Create(new AgentMailWebhookOptions
        {
            Secret = Svix.Secret,
            DeduplicationWindow = TimeSpan.FromHours(1)
        });
        _deduplicator = new InMemoryAgentMailWebhookDeduplicator(options, _time);
    }

    [Fact]
    public async Task SecondClaimOfSameDeliveryIsRejected()
    {
        Assert.True(await _deduplicator.TryClaimAsync("msg_1"));
        Assert.False(await _deduplicator.TryClaimAsync("msg_1"));
        Assert.True(await _deduplicator.TryClaimAsync("msg_2"));
    }

    [Fact]
    public async Task ReleasedDeliveryCanBeClaimedAgain()
    {
        Assert.True(await _deduplicator.TryClaimAsync("msg_1"));
        await _deduplicator.ReleaseAsync("msg_1");
        Assert.True(await _deduplicator.TryClaimAsync("msg_1"));
    }

    [Fact]
    public async Task ClaimExpiresAfterWindow()
    {
        Assert.True(await _deduplicator.TryClaimAsync("msg_1"));

        _time.Now += TimeSpan.FromMinutes(59);
        Assert.False(await _deduplicator.TryClaimAsync("msg_1"));

        _time.Now += TimeSpan.FromMinutes(2);
        Assert.True(await _deduplicator.TryClaimAsync("msg_1"));
    }

    [Fact]
    public async Task ConcurrentClaimsAdmitExactlyOne()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => _deduplicator.TryClaimAsync("msg_1").AsTask())));

        Assert.Single(results, claimed => claimed);
    }
}
