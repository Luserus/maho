# Syntax Statements Guide

The `Statements` folder contains AST node shapes for statements at both the top level and local scope.

## Main groups

### Shared base types

- `ReturnStatement`
- `TopLevelStatement`
- `LocalStatement`

### Top-level block and statement forms

- `TopLevelBlock`
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

Top-level statement nodes require file-level opt-in through `#pragma toplevel enable`. During
resolution, variables in an opted-in compilation unit are treated as locals of that file's
implicit `Main` function.

## How to traverse this folder

- Start with `TopLevelStatement` or `LocalStatement` depending on the scope you are tracing.
- Read the matching `If`, `While`, `Block`, or declaration statement type next.
- Pair this folder with [`../Expressions/docs/README.md`](../../Expressions/docs/README.md) when a statement wraps an expression.
