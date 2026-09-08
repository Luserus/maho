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

        foreach (var alias in context.AliasSymbols)
        {
            // Not implemented
        }

        foreach (var type in context.TypeSymbols)
        {
            ResolveTypeSymbol(type);
        }

        foreach (var func in context.FunctionSymbols)
        {
            // Not implemented
        }

        foreach (var global in context.GlobalVariableSymbols)
        {
            // Not implemented
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

        var body = typeSymbol.Syntax!.Body;
        
        ResolveTypeBody(body, typeSymbol);
    }

    private void ResolveNestedTypeSymbol(NestedTypeSymbol typeSymbol)
    {
        var attributes = typeSymbol.Syntax!.Attributes;
        typeSymbol.Attributes = ResolveAttributes(attributes, typeSymbol.EnclosingScope);

        var bases = typeSymbol.Syntax!.Base?.BaseTypes ?? [];

        typeSymbol.BaseTypes = ResolveBaseTypes(bases, typeSymbol.EnclosingScope);

        var constraints = typeSymbol.Syntax!.Constraints;
        var typeParameters = typeSymbol.TypeParameters;

        ResolveTypeConstraints(constraints, typeParameters);

        var body = typeSymbol.Syntax!.Body;
        
        ResolveNestedTypeBody(body, typeSymbol);
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
        if (typeParameters.Count == 0)
            return;

        var (_, id) = typeParameters[0];

        var typeParameter = context.TypeParameterSymbols[id];
        var constraints = new  Dictionary<SymbolPart, List<SymbolHandle>>();

        foreach (var constraintClause in typeConstraints)
        {
            var typeParamName = ResolutionContext.GetSymbolName(constraintClause.TypeParameter)[^1];

            foreach (var constraint in constraintClause.Constraints)
            {
                if (constraint is not TypeTypeConstraint typeConstraint)
                    continue;

                var name = ResolutionContext.GetSymbolName(typeConstraint.Type);

                var symbols = typeParameter.EnclosingScope[name];

                Debug.Assert(symbols.Count == 1);

                var symbol = symbols[0];

                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(constraints, typeParamName, out _);

                list ??= [];

                list.Add((symbol.Kind, symbol.ID));
            }
        }

        foreach (var (Kind, ID) in typeParameters)
        {
            typeParameter = context.TypeParameterSymbols[ID];

            var handles = constraints[typeParameter.Name];
            typeParameter.Constraints = handles;
        }
    }

    private void ResolveTypeBody(TypeBody body, TypeSymbol typeSymbol)
    {
        if (typeSymbol is ProductTypeSymbol productType)
            ResolveProductTypeBody(body, productType);
        else
            ResolveSumTypeBody(body, (SumTypeSymbol)typeSymbol);
    }

    private void ResolveNestedTypeBody(TypeBody body, NestedTypeSymbol typeSymbol)
    {
        if (typeSymbol is MemberProductTypeSymbol memberProductType)
            ResolveMemberProductTypeBody(body, memberProductType);
        else if (typeSymbol is MemberSumTypeSymbol memberSumType)
            ResolveMemberSumTypeBody(body, memberSumType);
        else if (typeSymbol is LocalProductTypeSymbol localProductType)
            ResolveLocalProductTypeBody(body, localProductType);
        else
            ResolveLocalSumTypeBody(body, (LocalSumTypeSymbol)typeSymbol);
    }

    private void ResolveProductTypeBody(TypeBody body, ProductTypeSymbol productType)
    {
        if (body is TypeBlockBody blockBody)
        {
            var members = blockBody.Members;

            foreach (var member in members)
            {
                if (member is MemberTypeDeclaration memberType)
                {
                    var name = ResolutionContext.GetSymbolName(memberType.Type.Name);

                    var scope = productType.EnclosingScope.GetChildScope((productType.Kind, productType.ID));

                    if (scope is null)
                        return; // TODO: Diagnostics

                    var symbols = scope[name];

                    Debug.Assert(symbols.Count == 1);

                    ResolveNestedTypeSymbol((NestedTypeSymbol)symbols[0]);
                }
            }
        }
    }

    private void ResolveSumTypeBody(TypeBody body, SumTypeSymbol sumType)
    {
        // Not implemented
    }

    private void ResolveMemberProductTypeBody(TypeBody body, MemberProductTypeSymbol productType)
    {
        if (body is TypeBlockBody blockBody)
        {
            var members = blockBody.Members;

            foreach (var member in members)
            {
                if (member is MemberTypeDeclaration memberType)
                {
                    var name = ResolutionContext.GetSymbolName(memberType.Type.Name);

                    var scope = productType.EnclosingScope.GetChildScope((productType.Kind, productType.ID));

                    if (scope is null)
                        return; // TODO: Diagnostics

                    var symbols = scope[name];

                    Debug.Assert(symbols.Count == 1);

                    ResolveNestedTypeSymbol((NestedTypeSymbol)symbols[0]);
                }
            }
        }
    }

    private void ResolveMemberSumTypeBody(TypeBody body, MemberSumTypeSymbol sumType)
    {
        // Not implemented
    }

    private void ResolveLocalProductTypeBody(TypeBody body, LocalProductTypeSymbol productType)
    {
        if (body is TypeBlockBody blockBody)
        {
            var members = blockBody.Members;

            foreach (var member in members)
            {
                if (member is MemberTypeDeclaration memberType)
                {
                    var name = ResolutionContext.GetSymbolName(memberType.Type.Name);

                    var scope = productType.EnclosingScope.GetChildScope((productType.Kind, productType.ID));

                    if (scope is null)
                        return; // TODO: Diagnostics

                    var symbols = scope[name];

                    Debug.Assert(symbols.Count == 1);

                    ResolveNestedTypeSymbol((NestedTypeSymbol)symbols[0]);
                }
            }
        }
    }

    private void ResolveLocalSumTypeBody(TypeBody body, LocalSumTypeSymbol sumType)
    {
        // Not implemented
    }
}