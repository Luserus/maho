# Syntax Statements Guide

The `Statements` folder contains AST node shapes for statements at both the top level and local scope.

## Main groups

### Shared base types

- `ReturnStatement`
- `TopLevelStatement`
- `LocalStatement`

### Top-level statement forms

- `TopLevelBlockStatement`
- `TopLevelElseStatement`
- `TopLevelEmptyStatement`
- `TopLevelExpressionStatement`
- `TopLevelIfStatement`
- `TopLevelReturnStatement`
- `TopLevelVariableDeclarationStatement`
- `TopLevelAmbiguousPointerDeclaration`
- `TopLevelAmbiguousReferenceDeclaration`
- `TopLevelWhileStatement`

### Local statement forms

- `LocalBlockStatement`
- `LocalElseStatement`
- `LocalEmptyStatement`
- `LocalExpressionStatement`
- `LocalIfStatement`
- `LocalReturnStatement`
- `LocalVariableDeclarationStatement`
- `LocalAmbiguousPointerDeclarationStatement`
- `LocalAmbiguousReferenceDeclarationStatement`
- `LocalWhileStatement`

## Design note

The duplication between top-level and local statement nodes is intentional and worth knowing about. The tree encodes grammar context in the type system instead of relying on:

- a shared statement node with mode flags,
- or later validation passes to decide where a statement was allowed.

That makes the syntax tree more explicit, even if it produces more files.

The payoff is that parser and later semantic code can tell whether a statement appeared in top-level or local scope without reconstructing that context from parent chains.

## How to traverse this folder

- Start with `TopLevelStatement` or `LocalStatement` depending on the scope you are tracing.
- Read the matching `If`, `While`, `Block`, or declaration statement type next.
- Pair this folder with [`../Expressions/docs/README.md`](../../Expressions/docs/README.md) when a statement wraps an expression.
