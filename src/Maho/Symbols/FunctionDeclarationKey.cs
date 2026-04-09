namespace Maho.Symbols;

/// <summary> Structural declaration key for a function inside one lexical scope. </summary>
/// <param name="Name">Source-backed declared name.</param>
/// <param name="Arity">Generic arity declared on the function name.</param>
/// <param name="ParameterSignatureKey">Normalized parameter-type signature.</param>
internal readonly record struct FunctionDeclarationKey(SymbolName Name, int Arity, string ParameterSignatureKey);
