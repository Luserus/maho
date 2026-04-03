using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Runs the configured semantic passes for one parsed compilation unit.
/// </summary>
internal sealed class Resolver
{
    private static readonly IReadOnlyList<ResolutionPass> Passes =
    [
        new SymbolDiscoveryPass()
    ];

    /// <summary>
    /// Resolves semantic state and appends semantic diagnostics to the shared diagnostics sink.
    /// </summary>
    public ResolutionResult Resolve(CompilationUnit root, DiagnosticsManager diagnostics)
    {
        ResolutionContext context = new(root, diagnostics);

        for (int i = 0; i < Passes.Count; i++)
            Passes[i].Execute(context);

        return context.ToResult();
    }
}
