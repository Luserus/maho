# Symbols Guide

The `Symbols` folder defines the compiler's semantic object model.

It is still compact, but it now contains the concrete symbol hierarchy that resolution uses to model namespaces, types, functions, parameters, type parameters, and variables.

## Files in this folder

- `Symbol.cs`: abstract base type for all symbols.
- `DeclaredSymbol.cs`: base type for symbols that come directly from syntax declarations.
- `NamespaceSymbol.cs`: namespace symbol implementation.
- `TypeSymbol.cs`: declared type symbol implementation.
- `FunctionSymbol.cs`: declared function symbol implementation.
- `ParameterSymbol.cs`: declared parameter symbol implementation.
- `TypeParameterSymbol.cs`: declared type parameter symbol implementation.
- `VariableSymbol.cs`: declared variable symbol implementation.
- `SymbolName.cs`: source-backed name wrapper used for equality and hashing without eager string allocation.
- `TypeDeclarationKey.cs`: stable identity for type declarations.
- `FunctionDeclarationKey.cs`: stable identity for function declarations.
- `SymbolKind.cs`: discriminator for the kinds of symbols the compiler models.

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

This enum is useful as a roadmap because it shows which semantic concepts the project represents in the current model.

### `Symbol`

Abstract base class with:

- `Kind`
- `Name`
- `ParentSymbol`

This tells you two important things about the intended design:

- symbols are expected to form a hierarchy,
- parentage is part of the core model, not an optional add-on.

`Name` is a source-backed `SymbolName`, not an eagerly materialized `string`. That lets resolution store and compare declaration names without allocating substrings up front. String materialization only happens when later output-oriented code actually needs text.

### `SymbolName`

This wrapper is worth reading closely because it is the allocation-sensitive part of the symbol model. It can be backed by:

- a `SourceText` span,
- or a literal string when the compiler synthesizes a name.

It keeps equality and hashing span-based so semantic tables do not need to allocate just to compare names.

## Traversal tip

- Read this folder before `Resolution` if you want to understand the semantic nouns.
- Read `Resolution` after this if you want to see where those nouns are wired into scopes, lookup, and pass scheduling.
