using AgentMail.AspNetCore;
using AgentMail.Client;
using AgentMail.Demo;
using Microsoft.Extensions.Options;

const string WebhookPath = "/webhooks/agentmail";
const long MaxWebhookBytes = 2 * 1024 * 1024; // AgentMail caps payloads at 1 MB.

var builder = WebApplication.CreateBuilder(args);

var webhookPort = builder.Configuration.GetValue("Demo:WebhookPort", 5080);
var dashboardPort = builder.Configuration.GetValue("Demo:DashboardPort", 5081);

// Both ports listen on loopback only. The Dev Tunnel forwards the webhook port; the dashboard
// (with its Approve and Replay actions) is never exposed.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenLocalhost(webhookPort);
    kestrel.ListenLocalhost(dashboardPort);
});

// Secrets come from user-secrets: AgentMail:ApiKey, AgentMail:WebhookSecret, Demo:AllowedRecipients:0.
builder.Services.AddAgentMail(o =>
{
    o.ApiKey = builder.Configuration["AgentMail:ApiKey"] ?? string.Empty;
    o.BaseAddress = builder.Configuration.GetValue<Uri?>("AgentMail:BaseAddress") ?? o.BaseAddress;
});

builder.Services.AddSingleton<DemoState>();
builder.Services.AddSingleton<InMemoryAgentMailWebhookDeduplicator>();
builder.Services.AddSingleton<IAgentMailWebhookDeduplicator, ReportingDeduplicator>();
builder.Services.AddAgentMailWebhooks(o => o.Secret = builder.Configuration["AgentMail:WebhookSecret"] ?? string.Empty);

builder.Services.AddSingleton<ICaseExtractor, RulesBasedDisputeExtractor>();
builder.Services.AddTransient<DisputeWorkflow>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("replay");

var app = builder.Build();

var state = app.Services.GetRequiredService<DemoState>();
state.AllowedRecipients = builder.Configuration.GetSection("Demo:AllowedRecipients").Get<string[]>() ?? [];

try
{
    var inbox = await app.Services.GetRequiredService<IAgentMailClient>().CreateInboxAsync(new CreateInboxRequest
    {
        ClientId = builder.Configuration["Demo:InboxClientId"],
        DisplayName = builder.Configuration["Demo:InboxDisplayName"]
    });
    state.InboxId = inbox.InboxId;
    state.InboxDisplayName = inbox.DisplayName;
}
catch (Exception ex) when (ex is AgentMailApiException or InvalidOperationException)
{
    Console.Error.WriteLine($"Could not open the demo inbox: {ex.Message}");
    Console.Error.WriteLine("""Check: dotnet user-secrets set "AgentMail:ApiKey" "am_..." --project samples/AgentMail.Demo""");
    return 1;
}

// Port gate: the webhook port answers only the webhook; the dashboard port never serves it
// and only accepts requests addressed to localhost (blocks DNS-rebinding style access).
app.Use(async (context, next) =>
{
    var port = context.Connection.LocalPort;
    var isWebhook = context.Request.Path.Equals(WebhookPath, StringComparison.OrdinalIgnoreCase);

    if (port == webhookPort && !(isWebhook && HttpMethods.IsPost(context.Request.Method)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (port == dashboardPort && (isWebhook || !IsLocalHost(context.Request.Host.Host)))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

// Webhook observer (demo only): keeps the exact bytes for "Replay same delivery" and reports
// deliveries the SDK endpoint rejected, without changing how the SDK processes them.
app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort != webhookPort)
    {
        await next();
        return;
    }

    if (context.Request.ContentLength > MaxWebhookBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    using var buffer = new MemoryStream();
    await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
    if (buffer.Length > MaxWebhookBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var body = buffer.ToArray();
    context.Request.Body = new MemoryStream(body);

    var headers = context.Request.Headers;
    var deliveryId = headers["svix-id"].ToString();
    var timestamp = headers["svix-timestamp"].ToString();
    context.Items[typeof(CapturedDelivery)] = new CapturedDelivery(
        body, deliveryId, timestamp, headers["svix-signature"].ToString(), CaseId: "", state.Now);

    await next();

    if (context.Response.StatusCode == StatusCodes.Status400BadRequest)
    {
        // An expired timestamp fails verification on its own, so "stale" is a definite cause;
        // anything else is reported without guessing which check failed.
        var tolerance = context.RequestServices.GetRequiredService<IOptions<AgentMailWebhookOptions>>().Value.TimestampTolerance;
        var stale = long.TryParse(timestamp, out var seconds) &&
                    (state.Now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > tolerance;
        var existing = state.FindByDelivery(deliveryId);
        var isOurReplay = state.Captured?.Id == deliveryId;

        state.Log("danger",
            stale ? (isOurReplay ? "Stale replay rejected" : "Stale delivery rejected") : "Webhook rejected before processing",
            stale
                ? $"Timestamp outside the {tolerance.TotalMinutes:0}-minute window · no processing occurred"
                : "Signature verification or payload validation failed · no processing occurred",
            existing?.Id);
    }
});

app.MapAgentMailWebhook(WebhookPath, (evt, services, _) =>
{
    var context = services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
    var raw = context.Items[typeof(CapturedDelivery)] as CapturedDelivery;
    services.GetRequiredService<DisputeWorkflow>().Handle(evt, raw?.Id ?? string.Empty, raw);
    return Task.CompletedTask;
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", () => Results.Json(state.Snapshot()));

app.MapGet("/api/events", async (HttpContext context) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var changes = state.Subscribe(out var unsubscribe);
    try
    {
        await context.Response.WriteAsync("data: changed\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);

        while (!context.RequestAborted.IsCancellationRequested)
        {
            using var keepAlive = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            keepAlive.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await changes.ReadAsync(keepAlive.Token);
                await context.Response.WriteAsync("data: changed\n\n", context.RequestAborted);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                await context.Response.WriteAsync(": keep-alive\n\n", context.RequestAborted);
            }

            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        unsubscribe();
    }
});

// State-changing actions require a custom header, which a cross-site page cannot send without a CORS preflight.
var actions = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
    ctx.HttpContext.Request.Headers.ContainsKey("X-Demo-Action") ? await next(ctx) : Results.StatusCode(StatusCodes.Status403Forbidden));

actions.MapPost("/cases/{caseId}/approve", async (string caseId, DisputeWorkflow workflow, CancellationToken ct) =>
{
    if (state.FindById(caseId) is null)
    {
        return Results.NotFound();
    }

    await workflow.ApproveAsync(caseId, ct);
    return Results.NoContent();
});

actions.MapPost("/replay", async (IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    if (state.Captured is not { } captured)
    {
        return Results.NotFound();
    }

    var age = state.Now - DateTimeOffset.FromUnixTimeSeconds(long.Parse(captured.Timestamp));
    state.Log("info", "Replay same delivery",
        $"Exact captured bytes and signature for {captured.CaseId} · signed {age.TotalSeconds:0}s ago", captured.CaseId);

    using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{webhookPort}{WebhookPath}")
    {
        Content = new ByteArrayContent(captured.Body)
    };
    request.Content.Headers.ContentType = new("application/json");
    request.Headers.Add("svix-id", captured.Id);
    request.Headers.Add("svix-timestamp", captured.Timestamp);
    request.Headers.Add("svix-signature", captured.Signature);

    using var response = await httpClientFactory.CreateClient("replay").SendAsync(request, ct);
    return Results.Json(new { status = (int)response.StatusCode });
});

app.Logger.LogInformation("Dashboard: http://localhost:{DashboardPort}  ·  Webhook: http://localhost:{WebhookPort}{Path} (expose only this port)",
    dashboardPort, webhookPort, WebhookPath);
app.Logger.LogInformation("Demo inbox: {Inbox} · reply allowlist: {Allowlist}",
    state.InboxId, state.AllowedRecipients.Count == 0 ? "(empty: replies blocked)" : string.Join(", ", state.AllowedRecipients));

await app.RunAsync();
return 0;

static bool IsLocalHost(string host) =>
    host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
