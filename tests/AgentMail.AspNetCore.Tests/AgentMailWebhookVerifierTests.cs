namespace AgentMail.AspNetCore.Tests;

public sealed class AgentMailWebhookVerifierTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private static bool Verify(
        string? signature = null,
        string? body = null,
        string? secret = null,
        string? messageId = null,
        string? timestamp = null,
        DateTimeOffset? now = null)
    {
        return AgentMailWebhookVerifier.Verify(
            secret ?? Svix.Secret,
            messageId ?? Svix.MessageId,
            timestamp ?? Svix.Timestamp,
            signature ?? Svix.Signature,
            body ?? Svix.Body,
            Tolerance,
            new ManualTimeProvider(now ?? Svix.SignedAt));
    }

    [Fact]
    public void AcceptsSvixReferenceSignature() => Assert.True(Verify());

    [Fact]
    public void AcceptsSecretWithoutPrefix() => Assert.True(Verify(secret: Svix.Secret["whsec_".Length..]));

    [Fact]
    public void AcceptsWhenAnyOfSeveralSignaturesMatches() =>
        Assert.True(Verify(signature: $"v1,Ym9ndXM= v2,ignored {Svix.Signature}"));

    [Fact]
    public void RejectsTamperedBody() => Assert.False(Verify(body: """{"test": 2432232315}"""));

    [Fact]
    public void RejectsDifferentMessageId() => Assert.False(Verify(messageId: "msg_other"));

    [Fact]
    public void RejectsWrongSecret() => Assert.False(Verify(secret: "whsec_" + Convert.ToBase64String(new byte[24])));

    [Fact]
    public void RejectsUnknownSignatureVersion() => Assert.False(Verify(signature: Svix.Signature.Replace("v1,", "v2,")));

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    public void RejectsTimestampOutsideTolerance(int minutesFromSigning) =>
        Assert.False(Verify(now: Svix.SignedAt.AddMinutes(minutesFromSigning)));

    [Theory]
    [InlineData(-4)]
    [InlineData(4)]
    public void AcceptsTimestampWithinTolerance(int minutesFromSigning) =>
        Assert.True(Verify(now: Svix.SignedAt.AddMinutes(minutesFromSigning)));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("99999999999999999")]
    public void RejectsInvalidTimestamp(string timestamp) => Assert.False(Verify(timestamp: timestamp));

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("v1,not base64!")]
    public void RejectsMalformedSignature(string signature) => Assert.False(Verify(signature: signature));

    [Theory]
    [InlineData("")]
    [InlineData("whsec_not base64!")]
    public void RejectsMissingOrMalformedSecret(string secret) => Assert.False(Verify(secret: secret));

    [Fact]
    public void RejectsMissingMessageId() => Assert.False(Verify(messageId: " "));
}
