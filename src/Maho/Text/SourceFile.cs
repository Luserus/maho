namespace Maho.Text;

internal readonly struct SourceFile
{
    public string FilePath { get; }
    public SourceTextLoadMode LoadMode { get; }

    public SourceFile(string filePath, SourceTextLoadMode loadMode = SourceTextLoadMode.Eager)
    {
        FilePath = filePath;
        LoadMode = loadMode;
    }
}