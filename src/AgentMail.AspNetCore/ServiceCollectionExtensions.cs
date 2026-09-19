using System.Net.Http.Headers;
using AgentMail.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentMail.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IAgentMailClient"/> for calling the AgentMail API.</summary>
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

    /// <summary>
    /// Registers webhook verification and duplicate-delivery protection used by
    /// <c>MapAgentMailWebhook</c>. Does not require an API key.
    /// </summary>
    public static IServiceCollection AddAgentMailWebhooks(
        this IServiceCollection services,
        Action<AgentMailWebhookOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<AgentMailWebhookOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Secret),
                "AgentMail webhook Secret must be configured.")
            .Validate(o => o.TimestampTolerance > TimeSpan.Zero,
                "AgentMail webhook TimestampTolerance must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAgentMailWebhookDeduplicator, InMemoryAgentMailWebhookDeduplicator>();

        return services;
    }
}
