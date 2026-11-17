namespace Maho.Syntax;

/// <summary>
/// Specifies the passing mode of an explicit instance receiver parameter (<c>this</c>).
/// </summary>
internal enum ReceiverKind : byte
{
    /// <summary>Not a receiver parameter (normal parameter).</summary>
    None = 0,

    /// <summary>Passed by value (<c>this</c>).</summary>
    Value,

    /// <summary>Passed as a safe storage alias (<c>&amp;this</c>).</summary>
    Alias,

    /// <summary>Passed as an exclusive mutable safe storage alias (<c>mut &amp;this</c>).</summary>
    MutAlias,

    /// <summary>Passed as an unsafe storage alias (<c>*this</c>).</summary>
    UnsafeAlias,

    /// <summary>Passed as a mutable unsafe storage alias (<c>mut *this</c>).</summary>
    MutUnsafeAlias,

    /// <summary>Passed as a shared/read-only safe pointer wrapper (<c>this&amp;</c>).</summary>
    Reference,

    /// <summary>Passed as a mutable safe pointer wrapper (<c>this mut&amp;</c>).</summary>
    MutReference,

    /// <summary>Passed as a raw pointer wrapper (<c>this*</c>).</summary>
    Pointer,

    /// <summary>Passed as a mutable raw pointer wrapper (<c>this mut*</c>).</summary>
    MutPointer
}
