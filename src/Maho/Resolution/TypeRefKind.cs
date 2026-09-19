namespace Maho.Resolution;

/// <summary>Represents the semantic state of a type reference in the compiler.</summary>
internal enum TypeRefKind : byte
{
    /// <summary>The type reference has not yet been processed by the resolution pass.</summary>
    Unresolved = 0,
    /// <summary>The type reference is omitted or 'var', pending expression-level type inference.</summary>
    Inferred,
    /// <summary>The type reference failed to resolve or is syntactically invalid.</summary>
    Error,
    /// <summary>The type reference was successfully resolved to a concrete symbol.</summary>
    Resolved
}
