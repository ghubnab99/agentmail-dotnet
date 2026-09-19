using System.Security.Cryptography;
using System.Text;

namespace AgentMail.AspNetCore;

public static class AgentMailWebhookVerifier
{
    public static bool Verify(
        string secret,
        string messageId,
        string timestamp,
        string signatureHeader,
        string rawBody,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(messageId) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var unixSeconds))
        {
            return false;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((DateTimeOffset.UtcNow - sentAt).Duration() > tolerance)
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

        var payload = Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{rawBody}");
        using var hmac = new HMACSHA256(key);
        var expected = hmac.ComputeHash(payload);

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
