using System.Collections.Generic;

namespace Maho.Resolution;

/// <summary>
/// Holds synthetic and fallback symbols produced during error recovery and macro desugaring.
/// </summary>
internal sealed class SymbolSink
{
    public List<AttributeSymbol> AttributeSymbols { get; } = [];
    public List<NestedAttributeSymbol> NestedAttributeSymbols { get; } = [];
    public List<TypeSymbol> TypeSymbols { get; } = [];
    public List<NestedTypeSymbol> NestedTypeSymbols { get; } = [];
    public List<FunctionSymbol> FunctionSymbols { get; } = [];
    public List<MethodSymbol> MethodSymbols { get; } = [];
    public List<GlobalVariableSymbol> GlobalVariableSymbols { get; } = [];
    public List<ConstantSymbol> ConstantSymbols { get; } = [];
    public List<FieldSymbol> FieldSymbols { get; } = [];
    public List<ParameterSymbol> ParameterSymbols { get; } = [];
    public List<PropertySymbol> PropertySymbols { get; } = [];
    public List<GenericParameterSymbol> GenericParameterSymbols { get; } = [];
    public List<LabelSymbol> LabelSymbols { get; } = [];
    public List<AliasSymbol> AliasSymbols { get; } = [];
    public List<MacroSymbol> MacroSymbols { get; } = [];
    public List<EnumVariantSymbol> EnumVariantSymbols { get; } = [];
}

