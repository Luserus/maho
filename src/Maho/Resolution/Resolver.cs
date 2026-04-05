using Maho.Diagnostics;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary> Runs the configured semantic passes for one parsed compilation unit or a whole project. </summary>
internal sealed class Resolver
{
    /// <summary>
    /// Shared diagnostic sink for the entire resolution run. The resolver follows the same model as
    /// lexer/parser: construction wires in ambient services, and <see cref="Resolve"/> only does semantic work. </summary>
    private readonly DiagnosticsManager diagnostics;
    /// <summary>
    /// The coordinator owns pass ordering and scheduling. The resolver itself is intentionally thin
    /// so the entrypoint stays stable even if pass orchestration changes later.
    /// </summary>
    private readonly ResolutionCoordinator coordinator;
    /// <summary>
    /// Concrete pass instances for this resolver. Keeping them per-resolver avoids accidentally
    /// sharing mutable pass state across unrelated resolution runs.
    /// </summary>
    private readonly ResolutionPass[] passes = [
        new SymbolDiscoveryPass()
    ];

    /// <summary> Creates a resolver that will report into the provided diagnostics manager. </summary>
    public Resolver(DiagnosticsManager diagnostics)
    {
        this.diagnostics = diagnostics;
        coordinator = new ResolutionCoordinator(passes);
    }

    /// <summary> Resolves semantic state for one compilation unit via the project-level coordinator. </summary>
    public ResolutionResult Resolve(CompilationUnit root) => Resolve(SyntaxTree.CreateSingleRoot(root)).Units[0];

    /// <summary> Resolves semantic state for every compilation unit captured by one syntax tree. </summary>
    public ResolutionProjectResult Resolve(SyntaxTree syntaxTree) => ResolveProject(new ResolutionProject(syntaxTree));

    /// <summary>
    /// Resolves semantic state for every compilation unit in a project. This is the real entrypoint
    /// once parsing has finished for the whole syntax tree.
    /// </summary>
    public ResolutionProjectResult ResolveProject(ResolutionProject project) => coordinator.Resolve(project, diagnostics);
}
