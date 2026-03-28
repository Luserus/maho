namespace Maho.Cli;

/// <summary>
/// Minimal executable entrypoint that forwards process arguments into the shared CLI pipeline.
/// Keeping startup logic here tiny prevents process concerns from leaking into command orchestration.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Delegates execution to <see cref="CommandLine"/> so one implementation owns parsing,
    /// analysis dispatch, and output behavior.
    /// </summary>
    private static int Main(string[] args) => CommandLine.Run(args);
}
