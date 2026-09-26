namespace Miryo;

/// <summary>
/// Minimal executable entrypoint for the Miryo build and project tool.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => MiryoCommandLine.Run(args);
}
