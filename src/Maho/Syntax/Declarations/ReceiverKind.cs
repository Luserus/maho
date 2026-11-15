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

    /// <summary>Passed as a storage alias (<c>ref this</c>).</summary>
    Alias,

    /// <summary>Passed as an exclusive mutable storage alias (<c>mut ref this</c>).</summary>
    MutAlias,

    /// <summary>Passed as a safe pointer wrapper (<c>this&amp;</c>).</summary>
    Reference,

    /// <summary>Passed as a mutable safe pointer wrapper (<c>mut this&amp;</c>).</summary>
    MutReference,

    /// <summary>Passed as a raw pointer wrapper (<c>this*</c>).</summary>
    Pointer
}

