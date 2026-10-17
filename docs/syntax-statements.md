# Syntax Statements Guide

The `Statements` folder contains AST node shapes for statements at both the top level and local scope.

## Main groups

### Shared base types

- `ReturnStatement`
- `TopLevelStatement`
- `LocalStatement`

### Top-level block and statement forms

- `TopLevelBlock`
- `TopLevelEmptyStatement`
- `TopLevelExpressionStatement`
- `TopLevelReturnStatement`
- `TopLevelVariableDeclarationStatement`

### Local statement forms

- `LocalEmptyStatement`
- `LocalExpressionStatement`
- `LocalReturnStatement`
- `LocalVariableDeclarationStatement`

## Design note

The duplication between top-level and local statement nodes is intentional and worth knowing about. The tree encodes grammar context in the type system instead of relying on:

- a shared statement node with mode flags,
- or later validation passes to decide where a statement was allowed.

That makes the syntax tree more explicit, even if it produces more files.

The payoff is that parser and later semantic code can tell whether a statement appeared in top-level or local scope without reconstructing that context from parent chains.

Note that blocks and control-flow statements (`if`, `while`) are unified into expressions (`BlockExpression`, `IfExpression`, `WhileExpression`) wrapped in `TopLevelExpressionStatement` or `LocalExpressionStatement`. When a block expression or an expression ending with a block (such as `if` or `while`) does not immediately precede `}` in an enclosing block, the compiler fabricates an expression statement without requiring a semicolon.

Top-level statement nodes require file-level opt-in through `#pragma toplevel enable`. During
resolution, variables in an opted-in compilation unit are treated as locals of that file's
implicit `Main` function.

Conditional headers in `IfExpression` and `WhileExpression` support either a simple `expression` condition or a `declaration; expression` syntax (e.g. `if (int x = f(); x > 0)` or `while (int x = f(); x > 0)`), scoping any declared variable to both the test expression and the then/body expression.

## How to traverse this folder

- Start with `TopLevelStatement` or `LocalStatement` depending on the scope you are tracing.
- Read the matching `Return`, `Goto`, `Label`, or declaration statement type next.
- Pair this guide with [`syntax-expressions.md`](syntax-expressions.md) when a statement wraps an expression.
