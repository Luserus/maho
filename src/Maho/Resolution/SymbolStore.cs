using System.Collections.Generic;

namespace Maho.Resolution;

internal struct SymbolStore
{
    public List<AttributeSymbol> AttributeSymbols;
    public List<NestedAttributeSymbol> NestedAttributeSymbols;
    public List<TypeSymbol> TypeSymbols;
    public List<NestedTypeSymbol> NestedTypeSymbols;
    public List<FunctionSymbol> FunctionSymbols;
    public List<MethodSymbol> MethodSymbols;
    public List<GlobalVariableSymbol> GlobalVariableSymbols;
    public List<FieldSymbol> FieldSymbols;
    public List<ParameterSymbol> ParameterSymbols;
    public List<LocalVariableSymbol> LocalVariableSymbols;
    public List<PropertySymbol> PropertySymbols;
    public List<GenericParameterSymbol> GenericParameterSymbols;
    public List<LabelSymbol> LabelSymbols;
    public List<AliasSymbol> AliasSymbols;
    public List<MacroSymbol> MacroSymbols;

    public SymbolStore(List<AttributeSymbol> attributeSymbols, List<NestedAttributeSymbol> nestedAttributeSymbols, List<TypeSymbol> typeSymbols, List<NestedTypeSymbol> nestedTypeSymbols,
    List<FunctionSymbol> functionSymbols, List<MethodSymbol> methodSymbols, List<GlobalVariableSymbol> globalVariableSymbols, List<FieldSymbol> fieldSymbols, List<ParameterSymbol> parameterSymbols,
    List<LocalVariableSymbol> localVariableSymbols, List<PropertySymbol> propertySymbols, List<GenericParameterSymbol> genericParameterSymbols, List<LabelSymbol> labelSymbols, List<AliasSymbol> aliasSymbols,
    List<MacroSymbol>? macroSymbols = null)
    {
        AttributeSymbols = attributeSymbols;
        NestedAttributeSymbols = nestedAttributeSymbols;
        TypeSymbols = typeSymbols;
        NestedTypeSymbols = nestedTypeSymbols;
        FunctionSymbols = functionSymbols;
        MethodSymbols = methodSymbols;
        GlobalVariableSymbols = globalVariableSymbols;
        FieldSymbols = fieldSymbols;
        ParameterSymbols = parameterSymbols;
        LocalVariableSymbols = localVariableSymbols;
        PropertySymbols = propertySymbols;
        GenericParameterSymbols = genericParameterSymbols;
        LabelSymbols = labelSymbols;
        AliasSymbols = aliasSymbols;
        MacroSymbols = macroSymbols ?? [];
    }

    /// <summary>
    /// Creates an empty symbol store with fresh collections for every symbol category.
    /// </summary>
    public static SymbolStore CreateEmpty() => new([], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
}