namespace Maho.Text;

/// <summary>
/// Describes one source file load request, including the file path and preferred load mode.
/// </summary>
internal readonly struct SourceFile
{
    /// <summary> File system path for the source file. </summary>
    public string FilePath { get; }
    /// <summary> How the file should be loaded into memory. </summary>
    public SourceTextLoadMode LoadMode { get; }

    /// <summary> Creates one source-file descriptor. </summary>
    public SourceFile(string filePath, SourceTextLoadMode loadMode = SourceTextLoadMode.Eager)
    {
        FilePath = filePath;
        LoadMode = loadMode;
    }
}
