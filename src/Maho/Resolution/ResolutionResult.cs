using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ResolutionResult
{
    public CompilationUnit Root { get; }
    public NamespaceSymbol GlobalNamespace { get; }
    public Scope GlobalScope { get; }
    public IReadOnlyDictionary<SyntaxNode, Scope> Scopes { get; }
    public IReadOnlyDictionary<SyntaxNode, Symbol> DeclaredSymbols { get; }
    public IReadOnlyDictionary<Symbol, Scope> SymbolScopes { get; }
    public IReadOnlyDictionary<TypeSyntax, ResolvedTypeReference> TypeReferences { get; }

    public ResolutionResult(
        CompilationUnit root,
        NamespaceSymbol globalNamespace,
        Scope globalScope,
        IReadOnlyDictionary<SyntaxNode, Scope> scopes,
        IReadOnlyDictionary<SyntaxNode, Symbol> declaredSymbols,
        IReadOnlyDictionary<Symbol, Scope> symbolScopes,
        IReadOnlyDictionary<TypeSyntax, ResolvedTypeReference> typeReferences)
    {
        Root = root;
        GlobalNamespace = globalNamespace;
        GlobalScope = globalScope;
        Scopes = scopes;
        DeclaredSymbols = declaredSymbols;
        SymbolScopes = symbolScopes;
        TypeReferences = typeReferences;
    }

    public bool TryResolveScope(SyntaxNode syntax, out Scope? scope) => Scopes.TryGetValue(syntax, out scope);

    public bool TryResolveDeclaredSymbol(SyntaxNode syntax, out Symbol? symbol) => DeclaredSymbols.TryGetValue(syntax, out symbol);

    public bool TryResolveSymbolScope(Symbol symbol, out Scope? scope) => SymbolScopes.TryGetValue(symbol, out scope);

    public bool TryResolveTypeReference(TypeSyntax syntax, out ResolvedTypeReference? typeReference) => TypeReferences.TryGetValue(syntax, out typeReference);
}
