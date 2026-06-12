using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Resolver
{
    private ResolutionPass[] passes =
    [
        new SymbolDiscoveryPass()
    ];

    private ResolvedTree resolvedTree = new ResolvedTree();
    private ResolutionMetadata metadata = new ResolutionMetadata();

    public void Resolve(SyntaxTree syntaxTree)
    {
        var context = new ResolutionContext(syntaxTree, resolvedTree, [NamespaceSymbol.Global], [Scope.GlobalScope], 1, 1);

        foreach (var pass in passes)
            pass.Resolve(context);

    }
}
