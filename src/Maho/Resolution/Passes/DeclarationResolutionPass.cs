using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class DeclarationResolutionPass : ResolutionPass
{
    private ResolutionContext context = null!;

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

        foreach (var type in context.TypeSymbols)
        {
            ResolveTypeSymbol(type);
        }
    }

    private void ResolveTypeSymbol(TypeSymbol typeSymbol)
    {
        var attributes = typeSymbol.Syntax!.Attributes;
        typeSymbol.Attributes = ResolveAttributes(attributes, typeSymbol.EnclosingScope);

        var bases = typeSymbol.Syntax!.Base?.BaseTypes ?? [];
        typeSymbol.BaseTypes = ResolveBaseTypes(bases, typeSymbol.EnclosingScope);

        var constraints = typeSymbol.Syntax!.Constraints;
        var typeParameters = typeSymbol.TypeParameters;

        ResolveTypeConstraints(constraints, typeParameters);
    }

    private static List<SymbolHandle> ResolveAttributes(IReadOnlyList<AttributeListSyntax> attributes, Scope enclosing)
    {
        var attributeSymbols = new List<SymbolHandle>(attributes.Count);

        foreach (var attr in attributes)
        {
            foreach (var a in attr.Attributes)
            {
                var name = ResolutionContext.GetSymbolName(a.Name);
                var symbols = enclosing[name];

                foreach (var symbol in symbols)
                    attributeSymbols.Add((symbol.Kind, symbol.ID));
            }
        }

        return attributeSymbols;
    }

    private static List<SymbolHandle> ResolveBaseTypes(SeparatedSyntaxList<TypeSyntax> bases, Scope enclosing)
    {        
        var baseSymbols = new List<SymbolHandle>(bases.Count);

        foreach (var b in bases)
        {
            var name = ResolutionContext.GetSymbolName(b);
            var symbols = enclosing[name];

            Debug.Assert(symbols.Count == 1);

            var symbol = symbols[0];


            baseSymbols.Add((symbol.Kind, symbol.ID));
        }

        return baseSymbols;
    }

    private void ResolveTypeConstraints(IReadOnlyList<TypeConstraintClause> typeConstraints, IReadOnlyList<SymbolHandle> typeParameters)
    {
        foreach (var (_, id) in typeParameters)
        {
            var typeParameter = context.TypeParameterSymbols[id];
            var constraints = new List<SymbolHandle>();

            foreach (var constraintClause in typeConstraints)
            {
                foreach (var constraint in constraintClause.Constraints)
                {
                    if (constraint is not TypeTypeConstraint typeConstraint)
                        continue;

                    var name = ResolutionContext.GetSymbolName(typeConstraint.Type);

                    if (name[^1] != typeParameter.Name)
                        continue;

                    var symbols = typeParameter.EnclosingScope[name];

                    Debug.Assert(symbols.Count == 1);

                    var symbol = symbols[0];

                    constraints.Add((symbol.Kind, symbol.ID));
                }
            }

            typeParameter.Constraints = constraints;
        }
    }
}