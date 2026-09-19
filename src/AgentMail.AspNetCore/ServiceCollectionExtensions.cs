using System.Net.Http.Headers;
using AgentMail.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentMail.AspNetCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentMail(
        this IServiceCollection services,
        Action<AgentMailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.AddHttpClient("AgentMail", (sp, httpClient) =>
        {
            var options = sp.GetRequiredService<IOptions<AgentMailOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException("AgentMail ApiKey must be configured.");
            }

            httpClient.BaseAddress = options.BaseAddress;
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.ApiKey);
        });

        services.AddTransient<IAgentMailClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AgentMailOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new AgentMailClient(factory.CreateClient("AgentMail"), options.ApiKey, options.BaseAddress);
        });

        return services;
    }
}
