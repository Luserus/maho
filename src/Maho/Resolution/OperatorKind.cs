namespace Maho.Resolution;

/// <summary>
/// Enumerates all overloadable operators recognized by the Maho compiler.
/// </summary>
internal enum OperatorKind
{
    None = 0,

    // Binary operators
    Addition,
    Subtraction,
    Multiplication,
    Division,
    Remainder,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    LeftShift,
    RightShift,
    RotateLeft,
    RotateRight,
    Equality,
    Inequality,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LogicalAnd,
    LogicalOr,

    // Unary non-mutating operators
    UnaryNegation,
    UnaryPositive,
    LogicalNot,
    BitwiseNot,

    // Increment and decrement operators
    PrefixIncrement,
    PostfixIncrement,
    PrefixDecrement,
    PostfixDecrement,

    // Pointer and reference operators
    AddressOf,
    Dereference,
    IndirectMemberAccess,

    // Indexers
    IndexerGetter,
    IndexerSetter,

    // Assignment and compound assignment operators
    Assignment,
    AdditionAssignment,
    SubtractionAssignment,
    MultiplicationAssignment,
    DivisionAssignment,
    RemainderAssignment,
    BitwiseAndAssignment,
    BitwiseOrAssignment,
    BitwiseXorAssignment,
    LeftShiftAssignment,
    RightShiftAssignment,
    RotateLeftAssignment,
    RotateRightAssignment,

    // Special lifecycle, allocation, and conversion operators
    Constructor,
    Destructor,
    ImplicitCast,
    ExplicitCast,
    New,
    Put,
    Copy,
    Move,
    Callable
}

