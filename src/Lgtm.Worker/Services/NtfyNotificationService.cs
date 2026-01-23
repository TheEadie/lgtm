using System.Text;

namespace Lgtm.Worker.Services;

public class NtfyNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string? _ntfyUrl;

    public NtfyNotificationService(HttpClient httpClient, string? ntfyUrl)
    {
        _httpClient = httpClient;
        _ntfyUrl = ntfyUrl;
    }

    public async Task NotifyPrMergedAsync(string prUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_ntfyUrl))
            return;

        var (repo, number) = ParsePrUrl(prUrl);
        var message = $"{repo}#{number} has been merged. No further action needed.";

        await SendNotificationAsync(
            title: "PR Merged",
            message: message,
            tags: "white_check_mark,merged",
            priority: "default",
            clickUrl: prUrl,
            cancellationToken);
    }

    public async Task NotifyReviewsAddressedAsync(string prUrl, int commentCount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_ntfyUrl))
            return;

        var (repo, number) = ParsePrUrl(prUrl);
        var message = $"{repo}#{number}: Addressed {commentCount} comments. PR is now a draft - please review and mark ready.";

        await SendNotificationAsync(
            title: "Action Required: Review Changes",
            message: message,
            tags: "pencil,review",
            priority: "high",
            clickUrl: prUrl,
            cancellationToken);
    }

    private async Task SendNotificationAsync(
        string title,
        string message,
        string tags,
        string priority,
        string clickUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _ntfyUrl);
            request.Content = new StringContent(message, Encoding.UTF8, "text/plain");
            request.Headers.Add("Title", title);
            request.Headers.Add("Tags", tags);
            request.Headers.Add("Priority", priority);
            request.Headers.Add("Click", clickUrl);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to send ntfy notification: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending ntfy notification: {ex.Message}");
        }
    }

    private static (string repo, int number) ParsePrUrl(string prUrl)
    {
        // Format: https://github.com/owner/repo/pull/123
        var parts = prUrl.Split('/');
        if (parts.Length >= 5)
        {
            var repo = parts[^3];
            if (int.TryParse(parts[^1], out var number))
            {
                return (repo, number);
            }
        }
        return ("unknown", 0);
    }
}
