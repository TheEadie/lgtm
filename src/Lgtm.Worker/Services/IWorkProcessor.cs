namespace Lgtm.Worker.Services;

public interface IWorkProcessor
{
    Task ProcessAsync(CancellationToken cancellationToken);
}
