namespace Maho.Symbols;

internal readonly record struct FunctionDeclarationKey(SymbolName Name, int Arity, string ParameterSignatureKey);
