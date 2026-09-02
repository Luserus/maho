using System;
using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    private ResolutionContext context = null!;

    public override void Resolve(ResolutionContext context)
    {
        this.context = context;

        foreach (var root in context.SyntaxTree.Roots)
            ResolveCompilationUnit(root);
    }

    private void ResolveCompilationUnit(CompilationUnit unit)
    {
        FunctionSymbol? topLevelMain = null;
        Scope topLevelMainScope = context.GlobalScope;

        if (PragmaDirective.EnablesTopLevelStatements(unit.Pragmas))
        {
            topLevelMainScope = context.CreateScope(context.GlobalScope);
            topLevelMain = context.CreateFunctionSymbol(context.GlobalScope, new SymbolPart("Main"), context.GlobalNamespace, syntax: null);
        }

        ResolveTopLevelScope(unit.Members, context.GlobalScope, context.GlobalNamespace, topLevelMain, topLevelMainScope);
    }

    private NamespaceTrieNode ResolveTopLevelScope(IReadOnlyList<TopLevel> members, Scope scope, NamespaceTrieNode containingNamespace,
                                                   FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        NamespaceTrieNode currentNamespace = containingNamespace;

        foreach (var member in members)
            currentNamespace = ResolveTopLevel(member, scope, currentNamespace, topLevelMain, topLevelMainScope);

        return currentNamespace;
    }

    private NamespaceTrieNode ResolveTopLevel(TopLevel topLevel, Scope scope, NamespaceTrieNode containingNamespace,
                                              FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        switch (topLevel)
        {
            case NamespaceDeclaration declaration:
                return ResolveNamespaceDeclaration(declaration, scope, containingNamespace, topLevelMain, topLevelMainScope);
            case TopLevelBlock block:
                ResolveTopLevelScope(block.Members, scope, containingNamespace, topLevelMain, topLevelMainScope);
                return containingNamespace;
            case TopLevelGlobalBlock block:
                ResolveTopLevelScope(block.Members, scope, containingNamespace, topLevelMain: null, topLevelMainScope);
                return containingNamespace;
            case TopLevelTypeDeclaration declaration:
                ResolveTopLevelTypeDeclaration(declaration.Type, scope, containingNamespace);
                return containingNamespace;
            case TopLevelFunctionDeclaration declaration:
                ResolveTopLevelFunctionDeclaration(declaration.Function, scope, containingNamespace);
                return containingNamespace;
            case TopLevelVariableDeclaration declaration:
                ResolveTopLevelVariableDeclaration(declaration.Declaration, scope, containingNamespace, topLevelMain, topLevelMainScope);
                return containingNamespace;
            default:
                return containingNamespace;
        }
    }

    private NamespaceTrieNode ResolveNamespaceDeclaration(NamespaceDeclaration declaration, Scope scope, NamespaceTrieNode containingNamespace,
                                                           FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        NamespaceTrieNode declaredNamespace = GetOrDeclareNamespace(containingNamespace, declaration.Name);

        if (declaration.Body is NamespaceEmptyBody)
            return declaredNamespace;

        NamespaceBlockBody body = (NamespaceBlockBody)declaration.Body;
        ResolveTopLevelScope(body.Members, scope, declaredNamespace, topLevelMain, topLevelMainScope);
        return containingNamespace;
    }

    private void ResolveTopLevelVariableDeclaration(VariableDeclaration declaration, Scope scope, NamespaceTrieNode containingNamespace,
                                                    FunctionSymbol? topLevelMain, Scope topLevelMainScope)
    {
        if (topLevelMain is null)
        {
            context.CreateGlobalVariableSymbol(scope, ResolutionContext.GetSymbolName(declaration.Identifier)[^1], containingNamespace, declaration);
            return;
        }

        LocalVariableSymbol symbol = context.CreateLocalVariableSymbol(topLevelMainScope, ResolutionContext.GetSymbolName(declaration.Identifier)[^1],
                                                                         ResolutionContext.GetHandle(topLevelMain), declaration);

        foreach (var mod in declaration.Modifiers)
        {
            if (mod.MatchingKind is MatchingKeywordKind.Public)
                symbol.Flags |= VariableFlags.Public;
            else if (mod.MatchingKind is MatchingKeywordKind.Internal)
                symbol.Flags |= VariableFlags.Internal;
            else if (mod.MatchingKind is MatchingKeywordKind.Static)
                symbol.Flags |= VariableFlags.Static;
            else if (mod.MatchingKind is MatchingKeywordKind.Const)
                symbol.Flags |= VariableFlags.Const;
        }

        topLevelMain.LocalVariables.Add(ResolutionContext.GetHandle(symbol));
    }

    private void ResolveTopLevelTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope typeScope = context.CreateScope(enclosingScope);
        NamespaceTrieNode declaredNamespace = GetDeclaredTypeNamespace(declaration.Name, containingNamespace);
        TypeSymbol symbol = context.CreateTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name)[^1], ToResolutionTypeKind(declaration.Kind),
                                                      declaredNamespace, declaration);

        symbol.TypeParameters = ResolveTypeParameters(declaration.Name, typeScope, symbol);

        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveMemberTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        Scope typeScope = context.CreateScope(enclosingScope);
        MemberNestedTypeSymbol symbol = context.CreateMemberNestedTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name)[^1], ToResolutionTypeKind(declaration.Kind),
                                                                               containingType, declaration);

        symbol.TypeParameters = ResolveTypeParameters(declaration.Name, typeScope, symbol);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveLocalTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, MethodSymbol? containingMethod)
    {
        Scope typeScope = context.CreateScope(enclosingScope);
        LocalTypeSymbol symbol = context.CreateLocalTypeSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Name)[^1], ToResolutionTypeKind(declaration.Kind),
                                                                containingMethod, declaration);

        symbol.TypeParameters = ResolveTypeParameters(declaration.Name, typeScope, symbol);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol));
    }

    private void ResolveTypeBody(TypeBody body, Scope scope, SymbolHandle containingType)
    {
        if (body is not TypeBlockBody block)
            return;

        foreach (var member in block.Members)
            ResolveMember(member, scope, containingType);
    }

    private void ResolveMember(Member member, Scope scope, SymbolHandle containingType)
    {
        switch (member)
        {
            case MemberBlockDeclaration block:
                foreach (var child in block.Members)
                    ResolveMember(child, scope, containingType);
                break;
            case MemberTypeDeclaration declaration:
                ResolveMemberTypeDeclaration(declaration.Type, scope, containingType);
                break;
            case MemberFunctionDeclaration declaration:
                ResolveMemberFunctionDeclaration(declaration.Function, scope, containingType);
                break;
            case MemberFieldDeclaration declaration:
                context.CreateFieldSymbol(scope, ResolutionContext.GetSymbolName(declaration.Declaration.Identifier)[^1], containingType, declaration.Declaration);
                break;
            case MemberPropertyDeclaration declaration:
                ResolvePropertyDeclaration(declaration, scope);
                break;
        }
    }

    private void ResolveTopLevelFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope functionScope = context.CreateScope(enclosingScope);
        FunctionSymbol symbol = context.CreateFunctionSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Signature.Identifier)[^1], containingNamespace,
                                                              declaration);

        symbol.TypeParameters = ResolveTypeParameters(declaration.Signature.Identifier, functionScope, symbol);
        DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), containingMethod: null);
    }

    private void ResolveMemberFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        Scope functionScope = context.CreateScope(enclosingScope);
        MemberMethodSymbol symbol = context.CreateMemberMethodSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Signature.Identifier)[^1], containingType, declaration);

        symbol.TypeParameters = ResolveTypeParameters(declaration.Signature.Identifier, functionScope, symbol);
        DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), symbol);
    }

    private void ResolveLocalFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, MethodSymbol? containingMethod)
    {
        Scope functionScope = context.CreateScope(enclosingScope);
        LocalFunctionSymbol symbol = context.CreateLocalFunctionSymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Signature.Identifier)[^1], containingMethod, declaration);

        symbol.TypeParameters = ResolveTypeParameters(declaration.Signature.Identifier, functionScope, symbol);
        DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), symbol);
    }

    private void ResolvePropertyDeclaration(MemberPropertyDeclaration declaration, Scope enclosingScope)
    {
        PropertySymbol symbol = context.CreatePropertySymbol(enclosingScope, ResolutionContext.GetSymbolName(declaration.Identifier)[^1], hasBacking: false, declaration);

        foreach (var accessor in declaration.Body.Accessors)
            ResolveFunctionBody(accessor.Body, context.CreateScope(enclosingScope), ResolutionContext.GetHandle(symbol), containingMethod: null);
    }

    private void DiscoverParameters(FunctionSignature signature, Scope scope, SymbolHandle containingFunction)
    {
        foreach (var param in signature.Parameters)
            context.CreateParameterSymbol(scope, ResolutionContext.GetSymbolName(param.Declarator.Identifier)[^1], containingFunction);
    }

    private void ResolveFunctionBody(FunctionBody body, Scope scope, SymbolHandle containingSymbol, MethodSymbol? containingMethod)
    {
        if (body is not FunctionBlockBody block)
            return;

        foreach (var local in block.Locals)
            ResolveLocal(local, scope, containingSymbol, containingMethod);
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingSymbol, MethodSymbol? containingMethod)
    {
        switch (local)
        {
            case LocalBlockStatement block:
            {
                Scope blockScope = context.CreateScope(scope);

                foreach (var child in block.Locals)
                    ResolveLocal(child, blockScope, containingSymbol, containingMethod);
                break;
            }
            case LocalTypeDeclaration declaration:
                ResolveLocalTypeDeclaration(declaration.Type, scope, containingMethod);
                break;
            case LocalFunctionDeclaration declaration:
                ResolveLocalFunctionDeclaration(declaration.Function, scope, containingMethod);
                break;
            case LocalVariableDeclarationStatement declaration:
                context.CreateLocalVariableSymbol(scope, ResolutionContext.GetSymbolName(declaration.Declaration.Identifier)[^1], containingSymbol, declaration.Declaration);
                break;
        }
    }

    private List<SymbolHandle> ResolveTypeParameters(NamedSyntax name, Scope scope, Symbol genericSymbol)
    {
        GenericName? genericName = GetGenericName(name);

        if (genericName is null)
            return [];

        var typeParameters = new List<SymbolHandle>(genericName.TypeParameters.Count);

        foreach (var typeParameter in genericName.TypeParameters)
        {
            var symbol = context.CreateTypeParameterSymbol(scope, new SymbolPart(typeParameter.Name), genericSymbol);
            typeParameters.Add((symbol.Kind, symbol.ID));
        }

        return typeParameters;
    }

    private static NamespaceTrieNode GetOrDeclareNamespace(NamespaceTrieNode containingNamespace, NamedSyntax name)
    {
        return name switch
        {
            SimpleName simpleName => ResolutionContext.GetOrDeclareNamespace(containingNamespace, new SymbolPart(simpleName.Name)),
            GenericName => containingNamespace,
            QualifiedName qualifiedName => GetOrDeclareQualifiedNamespace(containingNamespace, qualifiedName),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private NamespaceTrieNode GetDeclaredTypeNamespace(NamedSyntax name, NamespaceTrieNode containingNamespace)
    {
        if (name is not QualifiedName qualifiedName || qualifiedName.Parts.Count < 2)
            return containingNamespace;

        NamespaceTrieNode current = context.GlobalNamespace;

        for (int index = 0; index < qualifiedName.Parts.Count - 1; index++)
            current = GetOrDeclareNamespace(current, qualifiedName.Parts[index]);

        return current;
    }

    private static NamespaceTrieNode GetOrDeclareQualifiedNamespace(NamespaceTrieNode containingNamespace, QualifiedName name)
    {
        NamespaceTrieNode current = containingNamespace;

        foreach (var part in name.Parts)
            current = GetOrDeclareNamespace(current, part);

        return current;
    }

    private static GenericName? GetGenericName(NamedSyntax name)
    {
        return name switch
        {
            GenericName genericName => genericName,
            QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetGenericName(qualifiedName.Parts[^1]),
            _ => null
        };
    }

    private static TypeKind ToResolutionTypeKind(Maho.TypeKind kind)
    {
        return kind switch
        {
            Maho.TypeKind.Struct => TypeKind.Struct,
            Maho.TypeKind.Class => TypeKind.Class,
            Maho.TypeKind.Interface => TypeKind.Interface,
            Maho.TypeKind.Attribute => TypeKind.Attribute,
            Maho.TypeKind.Union => TypeKind.Union,
            Maho.TypeKind.Enum => TypeKind.Enum,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
