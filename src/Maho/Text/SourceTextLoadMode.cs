namespace Maho.Text;

/// <summary> Defines the mode for loading SourceText. </summary>
internal enum SourceTextLoadMode : byte
{
    Eager,  // decode entire file immediately
    LazyCached,    // memory-map and decode fully on first access
}
