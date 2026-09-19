using System.Net;

namespace AgentMail.Client;

public sealed class AgentMailApiException : HttpRequestException
{
    public AgentMailApiException(HttpStatusCode statusCode, string? responseBody)
        : base($"AgentMail API request failed with HTTP {(int)statusCode} ({statusCode}).", null, statusCode)
    {
        ResponseBody = responseBody;
    }

    public string? ResponseBody { get; }
}
