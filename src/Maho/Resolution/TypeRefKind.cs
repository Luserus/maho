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
    Resolved,
    /// <summary>Pointer type (<c>T*</c>).</summary>
    Pointer,
    /// <summary>Reference type (<c>T&amp;</c>).</summary>
    Reference,
    /// <summary>Dynamic array type (<c>T[]</c>).</summary>
    Array,
    /// <summary>Fixed-size inline array type (<c>T[size]</c>).</summary>
    FixedArray,
    /// <summary>Span type (<c>&amp;T</c>).</summary>
    Span,
    /// <summary>Optional type (<c>T?</c>).</summary>
    Optional,
    /// <summary>Tuple type (<c>(T1, T2)</c>).</summary>
    Tuple,
    /// <summary>Union type (<c>(T1 | T2)</c>).</summary>
    Union,
    /// <summary>Untagged sum type modifier (<c>untagged T</c>).</summary>
    Untagged,
    /// <summary>Function pointer type (<c>func(params) -&gt; ReturnType</c>).</summary>
    Function,
    /// <summary>Delegate type (<c>delegate(params) -&gt; ReturnType</c>).</summary>
    Delegate,
    /// <summary>Compile-time literal type (uncollapsed integer, float, string, or char literal).</summary>
    Literal
}
