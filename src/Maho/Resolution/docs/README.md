# Resolution Guide

`src/Maho/Resolution` is now the live semantic bridge between parsed syntax and the compiler's symbol model.

This folder is no longer just a placeholder. It already contains:

- pass orchestration,
- nested scope construction,
- declaration symbol discovery,
- declaration-site type resolution,
- and reusable semantic state for later passes.

## Current architecture

The entry point is `Resolver`.

`Resolver` builds a fresh `ResolutionContext`, runs each `ResolutionPass` in sequence, and returns a `ResolutionResult`.

That gives the subsystem a simple rule for growth:

- mutable build-time state lives in `ResolutionContext`,
- stable queryable output lives in `ResolutionResult`,
- and each semantic stage becomes its own `ResolutionPass`.

## Files in this folder

- `Resolver.cs`: pass coordinator.
- `ResolutionPass.cs`: base type for semantic passes.
- `ResolutionContext.cs`: mutable per-run state and resolution maps.
- `ResolutionResult.cs`: stable resolved output for later consumers.
- `Scope.cs`: lexical scope model with local declaration storage and outward lookup.
- `SymbolDiscoveryPass.cs`: first pass that predeclares symbols and resolves declaration signatures.
- `ResolvedTypeReference.cs`: semantic representation for resolved declaration-site type syntax.

## What pass 1 does today

`SymbolDiscoveryPass` runs in two phases per scope:

1. predeclare
2. resolve

That split matters because declarations in the same scope need to exist before their signatures are fully interpreted. The first pass therefore:

- creates namespace, type, function, type-parameter, parameter, and variable symbols,
- creates scopes for namespaces, types, functions, blocks, and embedded statement bodies,
- records syntax-to-symbol and syntax-to-scope associations,
- resolves function return types,
- resolves parameter types,
- resolves variable declaration types,
- resolves generic arity for type and function declarations,
- and computes declaration keys for overload-like function/type identity.

In other words, pass 1 does not just collect names. It establishes most declaration metadata that later passes will depend on.

## Scopes

`Scope` stores:

- `Parent`
- `OwnerSymbol`
- `Boundary`
- declared symbols
- child scopes

Lookup is lexical. `Lookup(name)` searches the current scope first and then walks outward through parent scopes.

The scope table intentionally stores same-name symbols together. Distinguishing legal overload sets from duplicates is a later semantic decision, not a scope-storage concern.

## Resolved declaration data

The first pass resolves several useful pieces of semantic information up front.

### Symbol identity

Symbols now carry:

- `Name`
- `ParentSymbol`
- metadata names
- qualified metadata names

That makes nested declarations and generic declarations stable to compare later.

### Type declarations

`TypeSymbol` carries:

- declaration arity,
- resolved type parameters,
- and a `TypeDeclarationKey`.

### Function declarations

`FunctionSymbol` carries:

- declaration arity,
- resolved type parameters,
- resolved parameters,
- resolved return type,
- parameter count,
- parameter signature key,
- and a `FunctionDeclarationKey`.

This is the current foundation for overload and duplicate-signature analysis.

### Type references

`ResolvedTypeReference` models resolved declaration-site type syntax.

Current shapes include:

- named type references,
- qualified type references,
- and modified type references.

These references also store candidate symbols, so later passes can distinguish:

- fully resolved cases,
- ambiguous cases,
- and unresolved cases

without reparsing the syntax.

## Resolution maps

`ResolutionContext` and `ResolutionResult` expose maps for:

- syntax node -> declared symbol
- syntax node -> scope
- symbol -> owning scope
- type syntax -> resolved type reference

That allows later passes to reuse the first-pass work directly instead of rediscovering declarations.

## What should land here next

Natural next passes include:

- duplicate declaration and duplicate signature diagnostics,
- identifier lookup for expression/name uses,
- generic argument arity validation,
- namespace/type/member access resolution,
- and type-directed semantic validation.

Those passes should consume the data built by `SymbolDiscoveryPass` rather than rebuilding declaration state themselves.

## Extension guidance

- If the feature introduces new declaration forms or new declaration-site metadata, extend `SymbolDiscoveryPass` and the symbol model.
- If the feature consumes existing declaration/scope/type-reference state, add a new `ResolutionPass`.
- If a new pass needs to reuse state later, store it in `ResolutionContext` and project it through `ResolutionResult`.

## Reading order

Recommended order:

1. `Scope.cs`
2. symbol types in `../Symbols`
3. `ResolutionContext.cs`
4. `SymbolDiscoveryPass.cs`
5. `Resolver.cs`
6. `ResolutionResult.cs`

That order follows the actual dependency direction in the subsystem.
