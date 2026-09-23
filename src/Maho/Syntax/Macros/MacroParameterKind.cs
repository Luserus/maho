namespace Maho.Syntax;

/// <summary> Specifies the syntactic classifier of a macro pattern parameter. </summary>
internal enum MacroParameterKind : byte
{
    Expression,
    Type,
    Identifier,
    Statement,
    Literal
}
