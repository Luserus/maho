namespace Maho.Resolution;

/// <summary>
/// Enumerates all overloadable operators recognized by the Maho compiler.
/// These map directly to library-declared interfaces decorated with <c>[OperatorFunction("...")]</c>.
/// </summary>
internal enum OperatorKind
{
    /// <summary>No operator.</summary>
    None = 0,

    // Binary arithmetic & bitwise operators
    /// <summary>Binary addition (<c>+</c>).</summary>
    Addition,
    /// <summary>Binary subtraction (<c>-</c>).</summary>
    Subtraction,
    /// <summary>Binary multiplication (<c>*</c>).</summary>
    Multiplication,
    /// <summary>Binary division (<c>/</c>).</summary>
    Division,
    /// <summary>Binary remainder / modulo (<c>%</c>).</summary>
    Remainder,
    /// <summary>Binary bitwise AND (<c>&amp;</c>).</summary>
    BitwiseAnd,
    /// <summary>Binary bitwise OR (<c>|</c>).</summary>
    BitwiseOr,
    /// <summary>Binary bitwise XOR (<c>^</c>).</summary>
    BitwiseXor,
    /// <summary>Binary left bit shift (<c>&lt;&lt;</c>).</summary>
    LeftShift,
    /// <summary>Binary right bit shift (<c>&gt;&gt;</c>).</summary>
    RightShift,
    /// <summary>Binary left bit rotation.</summary>
    RotateLeft,
    /// <summary>Binary right bit rotation.</summary>
    RotateRight,

    // Binary relational & logical operators
    /// <summary>Equality comparison (<c>==</c>).</summary>
    Equality,
    /// <summary>Inequality comparison (<c>!=</c>).</summary>
    Inequality,
    /// <summary>Less than comparison (<c>&lt;</c>).</summary>
    LessThan,
    /// <summary>Less than or equal comparison (<c>&lt;=</c>).</summary>
    LessThanOrEqual,
    /// <summary>Greater than comparison (<c>&gt;</c>).</summary>
    GreaterThan,
    /// <summary>Greater than or equal comparison (<c>&gt;=</c>).</summary>
    GreaterThanOrEqual,
    /// <summary>Logical short-circuit AND (<c>&amp;&amp;</c>).</summary>
    LogicalAnd,
    /// <summary>Logical short-circuit OR (<c>||</c>).</summary>
    LogicalOr,

    // Unary non-mutating operators
    /// <summary>Unary arithmetic negation (<c>-x</c>).</summary>
    UnaryNegation,
    /// <summary>Unary positive (<c>+x</c>).</summary>
    UnaryPositive,
    /// <summary>Unary logical NOT (<c>!x</c>).</summary>
    LogicalNot,
    /// <summary>Unary bitwise NOT (<c>~x</c>).</summary>
    BitwiseNot,

    // Increment and decrement operators
    /// <summary>Prefix increment (<c>++x</c>).</summary>
    PrefixIncrement,
    /// <summary>Postfix increment (<c>x++</c>).</summary>
    PostfixIncrement,
    /// <summary>Prefix decrement (<c>--x</c>).</summary>
    PrefixDecrement,
    /// <summary>Postfix decrement (<c>x--</c>).</summary>
    PostfixDecrement,

    // Pointer and reference operators
    /// <summary>Address-of operator (<c>&amp;x</c>).</summary>
    AddressOf,
    /// <summary>Pointer dereference operator (<c>*p</c>).</summary>
    Dereference,
    /// <summary>Indirect member access operator (<c>-&gt;</c>).</summary>
    IndirectMemberAccess,

    // Indexers
    /// <summary>Index accessor getter (<c>x[i]</c>).</summary>
    IndexerGetter,
    /// <summary>Index accessor setter (<c>x[i] = v</c>).</summary>
    IndexerSetter,

    // Assignment and compound assignment operators
    /// <summary>Simple assignment (<c>=</c>).</summary>
    Assignment,
    /// <summary>Compound addition assignment (<c>+=</c>).</summary>
    AdditionAssignment,
    /// <summary>Compound subtraction assignment (<c>-=</c>).</summary>
    SubtractionAssignment,
    /// <summary>Compound multiplication assignment (<c>*=</c>).</summary>
    MultiplicationAssignment,
    /// <summary>Compound division assignment (<c>/=</c>).</summary>
    DivisionAssignment,
    /// <summary>Compound remainder assignment (<c>%=</c>).</summary>
    RemainderAssignment,
    /// <summary>Compound bitwise AND assignment (<c>&amp;=</c>).</summary>
    BitwiseAndAssignment,
    /// <summary>Compound bitwise OR assignment (<c>|=</c>).</summary>
    BitwiseOrAssignment,
    /// <summary>Compound bitwise XOR assignment (<c>^=</c>).</summary>
    BitwiseXorAssignment,
    /// <summary>Compound left shift assignment (<c>&lt;&lt;=</c>).</summary>
    LeftShiftAssignment,
    /// <summary>Compound right shift assignment (<c>&gt;&gt;=</c>).</summary>
    RightShiftAssignment,
    /// <summary>Compound rotate left assignment.</summary>
    RotateLeftAssignment,
    /// <summary>Compound rotate right assignment.</summary>
    RotateRightAssignment,

    // Special lifecycle, allocation, and conversion operators
    /// <summary>Constructor operator (<c>Ctor</c> interface).</summary>
    Constructor,
    /// <summary>Destructor operator (<c>Destructor</c> interface).</summary>
    Destructor,
    /// <summary>Implicit conversion operator (<c>Implicit</c> interface).</summary>
    ImplicitCast,
    /// <summary>Explicit conversion operator (<c>Explicit</c> interface).</summary>
    ExplicitCast,
    /// <summary>Heap allocation operator (<c>New</c> interface invoked by <c>new&lt;...&gt; T(...)</c>).</summary>
    New,
    /// <summary>In-place allocation operator (<c>Put</c> interface invoked by <c>put&lt;...&gt; T(...)</c>).</summary>
    Put,
    /// <summary>Copy lifecycle operator (<c>Copy</c> interface).</summary>
    Copy,
    /// <summary>Move lifecycle operator (<c>Move</c> interface).</summary>
    Move,
    /// <summary>Callable invocation operator (<c>Callable</c> interface invoked by <c>x(args...)</c>).</summary>
    Callable
}


