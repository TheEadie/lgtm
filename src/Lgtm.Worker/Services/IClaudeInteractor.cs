namespace Lgtm.Worker.Services;

/// <summary>
/// Provides operations for interacting with the Claude CLI.
/// </summary>
public interface IClaudeInteractor
{
    /// <summary>
    /// Runs Claude CLI in streaming mode with a given prompt.
    /// </summary>
    /// <param name="prompt">The prompt to send to Claude.</param>
    /// <param name="workingDirectory">The working directory for Claude to operate in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when Claude CLI exits with a non-zero code.</exception>
    Task RunClaudeStreamingAsync(string prompt, string workingDirectory, CancellationToken cancellationToken);
}
