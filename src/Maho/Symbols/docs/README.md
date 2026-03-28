# Symbols Guide

The `Symbols` folder defines the beginning of the compiler's semantic object model.

It is intentionally small at the moment, but it already establishes the vocabulary that later resolution work will build on.

## Files in this folder

- `Symbol.cs`: abstract base type for all symbols.
- `SymbolKind.cs`: discriminator for the kinds of symbols the compiler expects to model.

## Type guide

### `SymbolKind`

Current symbol categories:

- `Namespace`
- `Type`
- `Function`
- `Parameter`
- `TypeParameter`
- `Variable`
- `Label`

This enum is useful as a roadmap because it shows which semantic concepts the project expects to represent even before concrete symbol subclasses exist.

### `Symbol`

Abstract base class with:

- `Kind`
- `ParentSymbol`

This tells you two important things about the intended design:

- symbols are expected to form a hierarchy,
- parentage is part of the core model, not an optional add-on.

The folder does not yet contain concrete symbol implementations, so think of it as the semantic type system's skeleton rather than a finished layer.

## Traversal tip

- Read this folder before `Resolution` if you want to understand the semantic nouns.
- Read `Resolution` after this if you want to see where those nouns are likely to be wired into scopes and lookup behavior later.
