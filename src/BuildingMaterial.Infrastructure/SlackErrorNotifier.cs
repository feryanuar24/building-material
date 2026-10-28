using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildingMaterial.Infrastructure;

public sealed class SlackErrorNotifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<SlackErrorNotifier> logger)
{
    public async Task NotifyAsync(Exception exception, string path, CancellationToken cancellationToken)
    {
        var webhookUrl = configuration["Slack:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        var payload = new
        {
            text = $"*Unhandled API error*\nPath: `{path}`\nException: `{exception.GetType().Name}`\nMessage: `{exception.Message}`"
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(webhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception notificationException) when (notificationException is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(notificationException, "Unable to send API error notification to Slack.");
        }
    }
}
