using System.Collections.Generic;

namespace Maho.Resolution;

internal struct SymbolStore
{
    public List<TypeSymbol> TypeSymbols;
    public List<NestedTypeSymbol> NestedTypeSymbols;
    public List<FunctionSymbol> FunctionSymbols;
    public List<MethodSymbol> MethodSymbols;
    public List<GlobalVariableSymbol> GlobalVariableSymbols;
    public List<FieldSymbol> FieldSymbols;
    public List<ParameterSymbol> ParameterSymbols;
    public List<LocalVariableSymbol> LocalVariableSymbols;
    public List<TypeParameterSymbol> TypeParameterSymbols;
    public List<LabelSymbol> LabelSymbols;
    public List<AliasSymbol> AliasSymbols;

    public SymbolStore(List<TypeSymbol> typeSymbols, List<NestedTypeSymbol> nestedTypeSymbols, List<FunctionSymbol> functionSymbols, List<MethodSymbol> methodSymbols,
    List<GlobalVariableSymbol> globalVariableSymbols, List<FieldSymbol> fieldSymbols, List<ParameterSymbol> parameterSymbols, List<LocalVariableSymbol> localVariableSymbols,
    List<TypeParameterSymbol> typeParameterSymbols, List<LabelSymbol> labelSymbols, List<AliasSymbol> aliasSymbols)
    {
        TypeSymbols = typeSymbols;
        NestedTypeSymbols = nestedTypeSymbols;
        FunctionSymbols = functionSymbols;
        MethodSymbols = methodSymbols;
        GlobalVariableSymbols = globalVariableSymbols;
        FieldSymbols = fieldSymbols;
        ParameterSymbols = parameterSymbols;
        LocalVariableSymbols = localVariableSymbols;
        TypeParameterSymbols = typeParameterSymbols;
        LabelSymbols = labelSymbols;
        AliasSymbols = aliasSymbols;
    }
}