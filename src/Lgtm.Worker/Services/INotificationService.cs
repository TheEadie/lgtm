namespace Lgtm.Worker.Services;

public interface INotificationService
{
    Task NotifyPrMergedAsync(string prUrl, CancellationToken cancellationToken);
    Task NotifyReviewsAddressedAsync(string prUrl, int commentCount, CancellationToken cancellationToken);
}
