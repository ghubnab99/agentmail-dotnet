using System.Security.Cryptography;
using System.Text;

namespace AgentMail.AspNetCore;

public static class AgentMailWebhookVerifier
{
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static bool Verify(
        string secret,
        string messageId,
        string timestamp,
        string signatureHeader,
        string rawBody,
        TimeSpan tolerance,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        return Verify(secret, messageId, timestamp, signatureHeader,
            Encoding.UTF8.GetBytes(rawBody), tolerance, timeProvider);
    }

    public static bool Verify(
        string secret,
        string messageId,
        string timestamp,
        string signatureHeader,
        ReadOnlySpan<byte> rawBody,
        TimeSpan tolerance,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(messageId) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var unixSeconds) ||
            unixSeconds < 0 ||
            unixSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((now - sentAt).Duration() > tolerance)
        {
            return false;
        }

        var encodedSecret = secret.StartsWith("whsec_", StringComparison.Ordinal)
            ? secret["whsec_".Length..]
            : secret;

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedSecret);
        }
        catch (FormatException)
        {
            return false;
        }

        // Svix signs "{svix-id}.{svix-timestamp}.{raw body bytes}".
        var prefix = Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.");
        var payload = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(payload, 0);
        rawBody.CopyTo(payload.AsSpan(prefix.Length));

        var expected = HMACSHA256.HashData(key, payload);

        foreach (var token in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split(',', 2);
            if (parts.Length != 2 || parts[0] != "v1")
            {
                continue;
            }

            try
            {
                if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(parts[1])))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
            }
        }

        return false;
    }
}
