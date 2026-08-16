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
            topLevelMain = context.CreateFunctionSymbol(context.GlobalScope, new SymbolName("Main"), context.GlobalNamespace, [], syntax: null);
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
            context.CreateGlobalVariableSymbol(scope, GetSymbolName(declaration.Identifier), containingNamespace, [], declaration);
            return;
        }

        LocalVariableSymbol symbol = context.CreateLocalVariableSymbol(topLevelMainScope, GetSymbolName(declaration.Identifier),
                                                                         ResolutionContext.GetHandle(topLevelMain), [], declaration);
        topLevelMain.LocalVariables.Add(ResolutionContext.GetHandle(symbol));
    }

    private void ResolveTopLevelTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope typeScope = context.CreateScope(enclosingScope);
        List<SymbolHandle> typeParameters = [];
        NamespaceTrieNode declaredNamespace = GetDeclaredTypeNamespace(declaration.Name, containingNamespace);
        TypeSymbol symbol = context.CreateTypeSymbol(enclosingScope, GetSymbolName(declaration.Name), ToResolutionTypeKind(declaration.Kind),
                                                      declaredNamespace, typeParameters, declaration);

        DiscoverTypeParameters(declaration.Name, typeScope, symbol, typeParameters);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol), typeParameters);
    }

    private void ResolveMemberTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        Scope typeScope = context.CreateScope(enclosingScope);
        List<SymbolHandle> typeParameters = [];
        MemberNestedTypeSymbol symbol = context.CreateMemberNestedTypeSymbol(enclosingScope, GetSymbolName(declaration.Name), ToResolutionTypeKind(declaration.Kind),
                                                                               containingType, typeParameters, declaration);

        DiscoverTypeParameters(declaration.Name, typeScope, symbol, typeParameters);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol), typeParameters);
    }

    private void ResolveLocalTypeDeclaration(TypeDeclaration declaration, Scope enclosingScope, MethodSymbol? containingMethod)
    {
        Scope typeScope = context.CreateScope(enclosingScope);
        List<SymbolHandle> typeParameters = [];
        LocalTypeSymbol symbol = context.CreateLocalTypeSymbol(enclosingScope, GetSymbolName(declaration.Name), ToResolutionTypeKind(declaration.Kind),
                                                                containingMethod, typeParameters, declaration);

        DiscoverTypeParameters(declaration.Name, typeScope, symbol, typeParameters);
        ResolveTypeBody(declaration.Body, typeScope, ResolutionContext.GetHandle(symbol), typeParameters);
    }

    private void ResolveTypeBody(TypeBody body, Scope scope, SymbolHandle containingType, IReadOnlyList<SymbolHandle> typeParameters)
    {
        if (body is not TypeBlockBody block)
            return;

        foreach (var member in block.Members)
            ResolveMember(member, scope, containingType, typeParameters);
    }

    private void ResolveMember(Member member, Scope scope, SymbolHandle containingType, IReadOnlyList<SymbolHandle> typeParameters)
    {
        switch (member)
        {
            case MemberBlockDeclaration block:
                foreach (var child in block.Members)
                    ResolveMember(child, scope, containingType, typeParameters);
                break;
            case MemberTypeDeclaration declaration:
                ResolveMemberTypeDeclaration(declaration.Type, scope, containingType);
                break;
            case MemberFunctionDeclaration declaration:
                ResolveMemberFunctionDeclaration(declaration.Function, scope, containingType);
                break;
            case MemberFieldDeclaration declaration:
                context.CreateFieldSymbol(scope, GetSymbolName(declaration.Declaration.Identifier), containingType, typeParameters, declaration.Declaration);
                break;
            case MemberPropertyDeclaration declaration:
                ResolvePropertyDeclaration(declaration, scope, typeParameters);
                break;
        }
    }

    private void ResolveTopLevelFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, NamespaceTrieNode containingNamespace)
    {
        Scope functionScope = context.CreateScope(enclosingScope);
        List<SymbolHandle> typeParameters = [];
        FunctionSymbol symbol = context.CreateFunctionSymbol(enclosingScope, GetSymbolName(declaration.Signature.Identifier), containingNamespace,
                                                              typeParameters, declaration);

        DiscoverTypeParameters(declaration.Signature.Identifier, functionScope, symbol, typeParameters);
        DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), containingMethod: null, typeParameters);
    }

    private void ResolveMemberFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, SymbolHandle containingType)
    {
        Scope functionScope = context.CreateScope(enclosingScope);
        List<SymbolHandle> typeParameters = [];
        MemberMethodSymbol symbol = context.CreateMemberMethodSymbol(enclosingScope, containingType, typeParameters, declaration);

        DiscoverTypeParameters(declaration.Signature.Identifier, functionScope, symbol, typeParameters);
        DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), symbol, typeParameters);
    }

    private void ResolveLocalFunctionDeclaration(FunctionDeclaration declaration, Scope enclosingScope, MethodSymbol? containingMethod)
    {
        Scope functionScope = context.CreateScope(enclosingScope);
        List<SymbolHandle> typeParameters = [];
        LocalFunctionSymbol symbol = context.CreateLocalFunctionSymbol(enclosingScope, containingMethod, typeParameters, declaration);

        DiscoverTypeParameters(declaration.Signature.Identifier, functionScope, symbol, typeParameters);
        DiscoverParameters(declaration.Signature, functionScope, ResolutionContext.GetHandle(symbol));
        ResolveFunctionBody(declaration.Body, functionScope, ResolutionContext.GetHandle(symbol), symbol, typeParameters);
    }

    private void ResolvePropertyDeclaration(MemberPropertyDeclaration declaration, Scope enclosingScope, IReadOnlyList<SymbolHandle> typeParameters)
    {
        PropertySymbol symbol = context.CreatePropertySymbol(enclosingScope, GetSymbolName(declaration.Identifier), hasBacking: false, typeParameters, declaration);

        foreach (var accessor in declaration.Body.Accessors)
            ResolveFunctionBody(accessor.Body, context.CreateScope(enclosingScope), ResolutionContext.GetHandle(symbol), containingMethod: null, typeParameters);
    }

    private void DiscoverParameters(FunctionSignature signature, Scope scope, SymbolHandle containingFunction)
    {
        foreach (var _ in signature.Parameters)
            context.CreateParameterSymbol(scope, containingFunction);
    }

    private void ResolveFunctionBody(FunctionBody body, Scope scope, SymbolHandle containingSymbol, MethodSymbol? containingMethod,
                                     IReadOnlyList<SymbolHandle> typeParameters)
    {
        if (body is not FunctionBlockBody block)
            return;

        foreach (var local in block.Locals)
            ResolveLocal(local, scope, containingSymbol, containingMethod, typeParameters);
    }

    private void ResolveLocal(Local local, Scope scope, SymbolHandle containingSymbol, MethodSymbol? containingMethod,
                              IReadOnlyList<SymbolHandle> typeParameters)
    {
        switch (local)
        {
            case LocalBlockStatement block:
            {
                Scope blockScope = context.CreateScope(scope);

                foreach (var child in block.Locals)
                    ResolveLocal(child, blockScope, containingSymbol, containingMethod, typeParameters);
                break;
            }
            case LocalTypeDeclaration declaration:
                ResolveLocalTypeDeclaration(declaration.Type, scope, containingMethod);
                break;
            case LocalFunctionDeclaration declaration:
                ResolveLocalFunctionDeclaration(declaration.Function, scope, containingMethod);
                break;
            case LocalVariableDeclarationStatement declaration:
                context.CreateLocalVariableSymbol(scope, GetSymbolName(declaration.Declaration.Identifier), containingSymbol, typeParameters, declaration.Declaration);
                break;
        }
    }

    private void DiscoverTypeParameters(NamedSyntax name, Scope scope, Symbol genericSymbol, List<SymbolHandle> typeParameters)
    {
        GenericName? genericName = GetGenericName(name);

        if (genericName is null)
            return;

        foreach (var typeParameter in genericName.TypeParameters)
        {
            TypeParameterSymbol symbol = context.CreateTypeParameterSymbol(scope, new SymbolName(typeParameter.Name), genericSymbol);
            typeParameters.Add(ResolutionContext.GetHandle(symbol));
        }
    }

    private static NamespaceTrieNode GetOrDeclareNamespace(NamespaceTrieNode containingNamespace, NamedSyntax name)
    {
        return name switch
        {
            SimpleName simpleName => ResolutionContext.GetOrDeclareNamespace(containingNamespace, new SymbolName(simpleName.Name)),
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

    private static SymbolName GetSymbolName(NamedSyntax name)
    {
        return name switch
        {
            SimpleName simpleName => new SymbolName(simpleName.Name),
            GenericName genericName => new SymbolName(genericName.Name),
            QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetSymbolName(qualifiedName.Parts[qualifiedName.Parts.Count - 1]),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private static GenericName? GetGenericName(NamedSyntax name)
    {
        return name switch
        {
            GenericName genericName => genericName,
            QualifiedName qualifiedName when qualifiedName.Parts.Count > 0 => GetGenericName(qualifiedName.Parts[qualifiedName.Parts.Count - 1]),
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
