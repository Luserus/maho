using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Resolver
{
    private ResolutionPass[] passes =
    [
        new SymbolDiscoveryPass()
    ];

    private readonly ResolvedTree resolvedTree = new ResolvedTree();

    public ResolutionContext Resolve(SyntaxTree syntaxTree)
    {
        var symbolStore = new SymbolStore([], [], [], [], [], [], [], [], [], [], [], []);
        var context = new ResolutionContext(syntaxTree, resolvedTree, new NamespaceTrieNode(), symbolStore, [new Scope(null)]);

        foreach (var pass in passes)
            pass.Resolve(context);

        return context;
    }
}
