namespace Maho.Syntax;

/// <summary>Classifies whether a generic parameter accepts types or compile-time values.</summary>
internal enum GenericParameterKind : byte
{
    Type,
    Integer,
    Float,
    Constant
}