using System.Security.Cryptography;
using System.Text;

namespace AgentMail.AspNetCore.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class Svix
{
    // Reference vector from the Svix libraries' own test suites.
    public const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    public const string MessageId = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    public const string Timestamp = "1614265330";
    public const string Body = """{"test": 2432232314}""";
    public const string Signature = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";

    public static readonly DateTimeOffset SignedAt = DateTimeOffset.FromUnixTimeSeconds(1614265330);

    public static string Sign(string secret, string messageId, string timestamp, string body)
    {
        var key = Convert.FromBase64String(secret["whsec_".Length..]);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{body}"));
        return "v1," + Convert.ToBase64String(hash);
    }
}
