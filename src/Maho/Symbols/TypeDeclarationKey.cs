namespace Maho.Symbols;

/// <summary> Structural declaration key for a type inside one lexical scope. </summary>
/// <param name="Name">Source-backed declared type name.</param>
/// <param name="Arity">Generic arity declared on the type name.</param>
internal readonly record struct TypeDeclarationKey(SymbolName Name, int Arity);
