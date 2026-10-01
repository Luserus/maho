namespace Maho.Syntax;

/// <summary>Enumerates all overloadable operator kinds.</summary>
public enum OperatorKind : byte
{
    // Binary arithmetic
    Add,            // +
    Subtract,       // -
    Multiply,       // *
    Divide,         // /
    Modulo,         // %

    // Unary prefix
    UnaryPlus,      // prefix +
    UnaryMinus,     // prefix -
    Dereference,    // prefix *
    PrefixIncrement,// prefix ++
    PrefixDecrement,// prefix --
    LogicalNot,     // prefix !
    BitwiseNot,     // prefix ~
    AddressOf,      // prefix &

    // Unary postfix
    PostfixIncrement, // postfix ++
    PostfixDecrement, // postfix --
    PostfixBang,      // postfix !
    PostfixQuestion,  // postfix ?

    // Callable
    Call,           // ()

    // Logical
    LogicalOr,      // ||
    LogicalAnd,     // &&

    // Equality
    Equal,          // ==
    NotEqual,       // !=

    // Compound assignment
    AddAssign,      // +=
    SubtractAssign, // -=
    MultiplyAssign, // *=
    DivideAssign,   // /=
    ModuloAssign,   // %=

    // Arrow
    Arrow,          // ->

    // Relational
    LessThan,       // <
    GreaterThan,    // >
    LessOrEqual,    // <=
    GreaterOrEqual, // >=

    // Shift
    LeftShift,      // <<
    RightShift,     // >>

    // Bitwise
    BitwiseOr,      // |
    BitwiseAnd,     // &
    BitwiseXor,     // ^

    // Object creation
    ObjectPut,      // put
    ObjectNew,      // new
}
