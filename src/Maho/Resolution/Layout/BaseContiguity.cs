namespace Maho.Resolution.Layout;

/// <summary>
/// Indicates whether a base sub-object is contiguous within the derived memory layout
/// (allowing thin reference conversion) or split (requiring dyn fat reference conversion).
/// </summary>
internal enum BaseContiguity
{
    Contiguous,
    Split
}
