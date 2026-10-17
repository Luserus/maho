namespace Maho.Resolution;

/// <summary>
/// Represents the hygiene context of a symbol or identifier in the resolution pipeline.
/// Non-macro (user-written) symbols have Root hygiene (Value == 0).
/// Macro-generated symbols receive unique non-zero IDs to make them uncollidable with user symbols.
/// </summary>
internal readonly record struct HygieneId(int Value) : System.IEquatable<HygieneId>
{
    /// <summary> Root hygiene context (user source code / non-macro symbols). </summary>
    public static readonly HygieneId Root = default;

    /// <summary> Gets whether this represents non-macro root code. </summary>
    public bool IsRoot => Value == 0;

    /// <summary> Gets whether this symbol was generated inside a macro expansion. </summary>
    public bool IsMacroGenerated => Value != 0;

    public override string ToString() => IsRoot ? "root" : $"#{Value}";

    public static implicit operator HygieneId(int value) => new(value);
    public static implicit operator int(HygieneId id) => id.Value;
}
