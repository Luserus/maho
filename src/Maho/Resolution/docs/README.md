# Resolution Guide

`src/Maho/Resolution` is the compiler's semantic coordination layer.

It now supports:

- project-level pass coordination,
- per-unit semantic state,
- shared project-wide symbol scopes,
- declaration discovery,
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
- `ResolutionContext.cs`: mutable per-unit semantic state and resolution maps.
- `ResolutionResult.cs`: stable per-unit semantic result.
- `Scope.cs`: lexical scope model with local declaration storage and outward lookup.
- `SymbolDiscoveryPass.cs`: first pass that predeclares symbols and builds the project-wide declaration graph.
- `ResolvedTypeReference.cs`: semantic representation reserved for later declaration/type-resolution passes.

## Pass model

`ResolutionPass` now exposes three hooks:

- `BeforeProject(...)`
- `ExecuteUnit(...)`
- `AfterProject(...)`

That gives later semantic work room to choose the right scheduling shape instead of forcing everything into the first pass or into a purely unit-local traversal.

Examples:

- A declaration-merging pass can do project-wide setup, then process units, then finalize.
- A pure body-checking pass can do all its work in `ExecuteUnit(...)`.
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

`SyntaxTree` itself now serves as the project-wide syntax boundary. Because it inherits
`SyntaxNode`, the global namespace and global scope can anchor directly to the root-of-roots node
instead of using a separate synthetic placeholder type.

### Unit-local

Each `ResolutionContext` owns:

- one `CompilationUnit`
- syntax node -> declared symbol
- syntax node -> scope

It also projects the shared project-wide state through convenience properties like `GlobalScope`, `GlobalNamespace`, `Diagnostics`, and `References`.

So the current design already distinguishes:

- data that must be shared across the whole project
- and data that is inherently local to one syntax tree

## What pass 1 does today

`SymbolDiscoveryPass` is still the first semantic pass, but it now runs under the coordinator as a per-unit pass against shared project state.

Inside each unit it still uses two phases:

1. predeclare
2. resolve

That split allows same-scope declarations to exist before later passes interpret signatures and bodies.

The first pass currently:

- creates namespace, type, function, type-parameter, parameter, and variable symbols
- creates scopes for namespaces, types, functions, blocks, and embedded statement bodies
- records syntax-to-symbol and syntax-to-scope associations
- resolves generic arity for type and function declarations
- reports duplicate type declarations
- and contributes declarations into shared project-wide scope state

This means the first pass is project-aware but still stays focused on symbol discovery. Later passes can handle type references, `var`, overload signatures, and similar semantic work in one place instead of fragmenting that logic across the early pipeline.

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

The scope table keys declarations by `SymbolName`, which is a source-backed name value rather than
an eagerly allocated `string`. That keeps pass-1 declaration storage and duplicate checks
allocation-free for names.

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

`ResolvedTypeReference` and the related per-unit type-reference map are still present, but they are intentionally unused by pass 1 now.

That infrastructure is reserved for later passes that resolve:

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
8. `ResolutionCoordinator.cs`
9. `Resolver.cs`
10. `ResolutionResult.cs`
11. `ResolutionProjectResult.cs`

That order reflects the current dependency direction of the semantic layer.
