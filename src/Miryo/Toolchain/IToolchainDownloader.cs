namespace Miryo.Toolchain;

/// <summary>
/// Abstraction for checking, downloading, and updating toolchains and libraries.
/// Built as an extensible interface so remote fetching can be added cleanly in the future.
/// </summary>
public interface IToolchainDownloader
{
    /// <summary> Checks if a newer version of the toolchain manager or SDK is available. </summary>
    bool CheckForUpdates(string currentVersion, out string? latestVersion);

    /// <summary> Downloads a toolchain package to a local staging directory. </summary>
    string? DownloadToolchain(string version);

    /// <summary> Downloads a library package. </summary>
    string? DownloadLibrary(string name, string version);
}

/// <summary>
/// Local/offline implementation of <see cref="IToolchainDownloader"/>.
/// </summary>
public sealed class LocalToolchainDownloader : IToolchainDownloader
{
    public bool CheckForUpdates(string currentVersion, out string? latestVersion)
    {
        latestVersion = null;
        return false;
    }

    public string? DownloadToolchain(string version) => null;

    public string? DownloadLibrary(string name, string version) => null;
}
