namespace Maho.Resolution.Ownership;

/// <summary>How a value is consumed when it is used as a value.</summary>
internal enum OwnershipKind : byte
{
    /// <summary>The value may be duplicated and the source remains initialized.</summary>
    Copy,
    /// <summary>The value is relocated and the source becomes uninitialized.</summary>
    Move
}

/// <summary>State of a storage place at a control-flow program point.</summary>
internal enum InitializationState : byte
{
    Uninitialized,
    Initialized,
    Moved,
    MaybeInitialized
}

/// <summary>Classification of an ownership operation in a lowered body.</summary>
internal enum OwnershipStatementKind : byte
{
    StorageLive,
    StorageDead,
    Use,
    Borrow,
    Assign,
    Drop
}

/// <summary>How a use reads a place.</summary>
internal enum OwnershipUseKind : byte
{
    Copy,
    Move,
    Read
}

/// <summary>How a safe borrow accesses its referent.</summary>
internal enum BorrowKind : byte
{
    Shared,
    Mutable
}
