using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Resolver
{
    private static readonly IReadOnlyList<ResolutionPass> Passes =
    [
        new SymbolDiscoveryPass()
    ];

    public ResolutionResult Resolve(CompilationUnit root)
    {
        ResolutionContext context = new(root);

        for (int i = 0; i < Passes.Count; i++)
            Passes[i].Execute(context);

        return context.ToResult();
    }
}
