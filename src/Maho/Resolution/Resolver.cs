using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Resolution.Passes;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Orchestrates the semantic analysis and resolution pipeline of the Maho compiler.
/// <para>
/// Executes the 6 resolution passes in strict dependency order:
/// <list type="number">
/// <item><description><see cref="MacroExpansionPass"/>: Expands syntactic macros with hygiene tracking.</description></item>
/// <item><description><see cref="SymbolDiscoveryPass"/>: Discovers declarations and builds the lexical scope tree.</description></item>
/// <item><description><see cref="IntrinsicDiscoveryPass"/>: Validates compiler intrinsic attributes, types, and operator interfaces.</description></item>
/// <item><description><see cref="DeclarationResolutionPass"/>: Resolves member signatures, field/param types, and base types.</description></item>
/// <item><description><see cref="ExpressionResolutionPass"/>: Performs expression typing, operator overload resolution, and eager retroactive literal collapsing.</description></item>
/// <item><description><see cref="ControlFlowAndReturnInferencePass"/>: Constructs control flow graphs (CFGs), checks reachability/dead code, and infers function return types.</description></item>
/// </list>
/// </para>
/// </summary>
internal sealed class Resolver
{
    private readonly ResolutionPass[] passes =
    [
        new MacroExpansionPass(),
        new SymbolDiscoveryPass(),
        new IntrinsicDiscoveryPass(),
        new DeclarationResolutionPass(),
        new ExpressionResolutionPass(),
        new ControlFlowAndReturnInferencePass()
    ];

    private readonly ResolvedTree resolvedTree = new ResolvedTree();

    /// <summary>
    /// Executes the full resolution pipeline across a list of syntax trees.
    /// </summary>
    /// <param name="syntaxTrees">The parsed syntax trees representing source compilation units.</param>
    /// <param name="referencedProjects">Referenced compiled project contexts providing external symbols and intrinsics.</param>
    /// <param name="baseContext">Optional base context from an earlier compilation stage.</param>
    /// <param name="diagnostics">Optional diagnostics manager for error and warning aggregation.</param>
    /// <param name="options">Optional compilation configuration options.</param>
    /// <returns>A fully resolved <see cref="ResolutionContext"/> containing the semantic model, symbol tables, and CFGs.</returns>
    public ResolutionContext Resolve(
        IReadOnlyList<SyntaxTree> syntaxTrees,
        IReadOnlyList<ResolutionContext>? referencedProjects = null,
        ResolutionContext? baseContext = null,
        DiagnosticsManager? diagnostics = null,
        CompilationOptions? options = null)
    {
        var resolved = baseContext?.ResolvedTree ?? resolvedTree;
        var globalNamespace = baseContext?.GlobalNamespace ?? new NamespaceTrieNode();
        SymbolStore symbolStore;
        List<Scope> scopes;

        if (baseContext is not null)
        {
            symbolStore = new SymbolStore(
                baseContext.AttributeSymbols,
                baseContext.NestedAttributeSymbols,
                baseContext.TypeSymbols,
                baseContext.NestedTypeSymbols,
                baseContext.FunctionSymbols,
                baseContext.MethodSymbols,
                baseContext.GlobalVariableSymbols,
                baseContext.FieldSymbols,
                baseContext.ParameterSymbols,
                baseContext.PropertySymbols,
                baseContext.GenericParameterSymbols,
                baseContext.LabelSymbols,
                baseContext.AliasSymbols,
                baseContext.MacroSymbols
            );
            scopes = baseContext.Scopes;
        }
        else
        {
            symbolStore = SymbolStore.CreateEmpty();
            scopes = [new Scope(null)];
        }

        var context = new ResolutionContext(
            syntaxTrees,
            resolved,
            globalNamespace,
            symbolStore,
            scopes,
            referencedProjects ?? baseContext?.ReferencedProjects,
            baseContext?.ImportedProjectSymbols,
            diagnostics ?? baseContext?.Diagnostics,
            options ?? baseContext?.Options,
            baseContext?.DiscoveredIntrinsics.Clone(),
            baseContext?.DiscoveredOperators.Clone());

        foreach (var pass in passes)
            pass.Resolve(context);

        return context;
    }

    public ResolutionContext Resolve(
        SyntaxTree syntaxTree,
        IReadOnlyList<ResolutionContext>? referencedProjects = null,
        ResolutionContext? baseContext = null,
        DiagnosticsManager? diagnostics = null,
        CompilationOptions? options = null) =>
        Resolve([syntaxTree], referencedProjects, baseContext, diagnostics, options);
}
