# Resolution Guide

`src/Maho/Resolution` is the compiler's semantic coordination layer.

It now supports:

- project-level pass coordination,
- per-unit semantic state,
- shared project-wide symbol scopes,
- declaration discovery,
- type-hierarchy resolution,
- and infrastructure for later cross-project lookup.

## Current architecture

The public semantic entrypoint inside the front-end is still `Resolver`, but `Resolver` is now only a facade.
Like `Lexer` and `Parser`, it is created with a shared `DiagnosticsManager`, then `Resolve(...)`
runs the actual semantic work.

The actual orchestration happens in `ResolutionCoordinator`.

The model is split into four layers:

- `SyntaxTree`: post-parse syntax root for all compilation units
- `ResolutionProject`: input model for one project-wide resolution run
- `ResolutionCoordinatorContext`: mutable shared project state
- `ResolutionContext`: mutable per-compilation-unit state

That split matters because not every semantic pass wants the same granularity.

Some passes need:

- project-wide setup before touching files,
- per-file work after a global declaration barrier,
- or project-wide finalization after every file has contributed state

The pass API is built around that instead of assuming every pass is purely per-file.

## Files in this folder

- `Resolver.cs`: thin facade over the project-level coordinator.
- `ResolutionCoordinator.cs`: runs passes across the project.
- `ResolutionCoordinatorContext.cs`: shared mutable state for one project resolution run.
- `ResolutionProject.cs`: input container for a syntax tree and project references.
- `ResolutionProjectResult.cs`: stable project-wide semantic result.
- `ResolutionProjectReference.cs`: external project semantic surface for future cross-project lookup.
- `ResolutionPass.cs`: base type for semantic passes with project and unit hooks.
- `ResolutionExecutionMode.cs`: scheduler hint for how a pass wants its unit work to run.
- `ResolutionContext.cs`: mutable per-unit semantic state and resolution maps.
- `ResolutionPassUnitResult.cs`: base type for attach/merge unit results.
- `ResolutionResult.cs`: stable per-unit semantic result.
- `Scope.cs`: lexical scope model with local declaration storage and outward lookup.
- `SymbolDiscoveryPass.cs`: first pass that builds unit-local declaration graphs and attaches them into the project-wide declaration graph.
- `TypeHierarchyResolutionPass.cs`: second pass that resolves direct type-hierarchy edges and performs project-wide cycle detection.
- `ResolvedTypeReference.cs`: semantic representation used by declaration-site type-resolution passes.

## Pass model

`ResolutionPass` now exposes three hooks:

- `BeforeProject(...)`
- `ExecuteUnit(...)`
- `AfterProject(...)`

It also exposes an execution mode that tells the coordinator how unit work is scheduled:

- `Sequential`: unit work mutates shared project state directly, so the pass runs one unit at a time.
- `ParallelUnitLocal`: each unit can run independently because it only reads frozen shared state and writes unit-local state.
- `ParallelCollectThenMerge`: units first build unit-local results in parallel, then the coordinator attaches those results into project state sequentially.

That gives later semantic work room to choose the right scheduling shape instead of forcing everything into the first pass or into a purely unit-local traversal.

Examples:

- A declaration-building pass can build unit-local graphs in parallel, then attach them in project order.
- A pure body-checking pass can do all its work in `ExecuteUnit(...)` with `ParallelUnitLocal`.
- A cross-project validation pass can read project references during `AfterProject(...)`.

## Project-wide vs unit-local state

### Project-wide

`ResolutionCoordinatorContext` owns:

- project name
- shared diagnostics sink
- syntax-tree root
- global namespace symbol
- global scope
- project references
- shared symbol-to-scope table
- all unit contexts participating in the run

This is the layer that allows declarations from different files to land in one shared namespace/scope graph after parsing has already finished for the whole syntax tree.

`SyntaxTree` itself serves as the project-wide syntax boundary. Because it inherits `SyntaxNode`, the global namespace and global scope can anchor directly to the root-of-roots node instead of using a separate synthetic placeholder type.

### Unit-local

Each `ResolutionContext` owns:

- one `CompilationUnit`
- syntax node -> declared symbol
- syntax node -> scope
- type syntax -> resolved type reference

It also projects the shared project-wide state through convenience properties like `GlobalScope`, `GlobalNamespace`, `Diagnostics`, and `References`.

So the current design already distinguishes:

- data that must be shared across the whole project
- and data that is inherently local to one syntax tree

## What pass 1 does today

`SymbolDiscoveryPass` is the first semantic pass, and it runs as a parallel build plus sequential attach pass.

Each compilation unit first builds a fully declared unit-local graph:

- namespace symbols and scopes
- type symbols and owned scopes
- function symbols and owned scopes
- type parameters
- parameters
- variables
- block scopes
- embedded statement scopes

That unit-local graph uses real `Symbol` and `Scope` objects, but it stays isolated from shared
project state while collection is running.

After every unit has built its local graph, merge attaches those graphs into canonical project-wide
state:

- namespace paths are canonicalized so multiple units contribute to the same namespace symbols
- top-level declarations are reattached under the final global or namespace containers
- owned scopes are reparented into the canonical lexical tree
- syntax-to-symbol and syntax-to-scope associations are written into each unit result

Duplicate declaration diagnostics are intentionally deferred for now. That keeps pass 1 focused on
building the declaration graph without prematurely choosing language rules for partial declarations,
forward declarations, or future merging behavior.

This keeps the expensive declaration-building work parallel while still producing one deterministic
project-wide symbol graph for later passes.

## What pass 2 does today

`TypeHierarchyResolutionPass` is the second semantic pass, and it runs as a parallel unit-local
binding stage followed by sequential project-wide finalization.

Each compilation unit walks every declared type after symbol discovery has already established the
canonical symbol/scope graph. During that walk the pass:

- resolves every base-type syntax in the declaration's base list,
- stores the resulting `ResolvedTypeReference` objects in the unit-local type-reference map,
- stores the canonical direct hierarchy edges on the owning `TypeSymbol`,
- and keeps the hierarchy model intentionally simple by using one direct `BaseTypes` array instead of hard-coding stronger language categories.

After every unit has finished its local work, `AfterProject(...)` runs one whole-project cycle check
over the canonical type graph and reports diagnostics for each participating type.

## Cross-project infrastructure

Cross-project lookup is not implemented yet, but the coordinator now keeps explicit room for it.

`ResolutionProject` accepts `ResolutionProjectReference` items, and those references expose:

- referenced project name
- referenced global namespace
- referenced global scope

That is enough for later passes to start consulting external project symbol graphs without redesigning the coordinator.

So the current infrastructure anticipates:

- one project producing symbols
- another project consuming them as references

even though no pass uses that path yet.

## Scopes

`Scope` stores:

- `Parent`
- `OwnerSymbol`
- `Boundary`
- declared symbols
- child scopes

Lookup is lexical. `Lookup(name)` searches the current scope first and then walks outward through parent scopes.

The scope table keys declarations by `SymbolName`, which is a source-backed name value rather than an eagerly allocated `string`. That keeps pass-1 declaration storage allocation-free for names.

The scope table intentionally stores same-name symbols together. Distinguishing legal overload sets from duplicates is a semantic-pass concern, not a storage concern.

## Results

There are now two semantic result shapes:

- `ResolutionResult`: one compilation unit
- `ResolutionProjectResult`: the whole coordinated project run

That split matches the coordinator model:

- unit consumers can stay focused on one tree
- project-wide consumers can inspect shared scopes and references

## Extension guidance

- If the feature introduces new declaration forms or new symbol-shape metadata, extend `SymbolDiscoveryPass` and the symbol model.
- If the feature needs project barriers, use `BeforeProject(...)` and `AfterProject(...)`.
- If the feature is naturally file-local once project declarations exist, implement it in `ExecuteUnit(...)`.
- If the feature needs external symbols, consume `ResolutionProjectReference` from the unit or project context rather than inventing a second coordination path.

## Deferred work

`ResolvedTypeReference` and the related per-unit type-reference map now participate in
`TypeHierarchyResolutionPass`, but they still leave room for later declaration- and type-oriented passes.

That infrastructure can continue to support passes that resolve:

- declaration-site type references,
- `var`,
- overload signatures,
- and other type-directed semantic behavior

after symbol discovery has already completed across the project.

## Reading order

Recommended order:

1. `ResolutionProject.cs`
2. `ResolutionCoordinatorContext.cs`
3. `ResolutionContext.cs`
4. `Scope.cs`
5. symbol types in `../Symbols`
6. `ResolutionPass.cs`
7. `SymbolDiscoveryPass.cs`
8. `TypeHierarchyResolutionPass.cs`
9. `ResolutionCoordinator.cs`
10. `Resolver.cs`
11. `ResolutionResult.cs`
12. `ResolutionProjectResult.cs`

That order reflects the current dependency direction of the semantic layer.
