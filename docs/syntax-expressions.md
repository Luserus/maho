# Syntax Expressions Guide

The `Expressions` folder contains the AST node shapes for expression syntax.

These files are intentionally declarative: they define the tree that the parser produces, not the parser logic that recognizes the grammar.

## Common anchors

- `Expression.cs`: base type for all expression nodes.
- `NamedExpression.cs`: shared base for expression forms tied to names.

## Expression families in this folder

- Name/reference forms:
  `IdentifierNameExpression`, `GenericNameExpression` (supports standard `<...>` as well as explicit turbofish `::<...>`)
- Literal/grouping forms:
  `LiteralExpression`, `ParenthesizedExpression`, `TupleExpression`
- Operator forms:
  `UnaryExpression`, `BinaryExpression`, `AssignmentExpression`, `AsExpression`
- Access/call forms:
  `MemberAccessExpression`, `CallExpression`, `IndexExpression`, `NamedArgumentExpression`
- Control-flow-like forms:
  `IfExpression`, `ElseExpression`, `WhileExpression`, `BlockExpression`
- Lambda and closure forms:
  `LambdaExpression`, `LambdaParameterSyntax`
- Construction forms:
  `ArrayCreationExpression`, `ObjectCreationExpression`, `ConstructorCallExpression`, `CollectionExpression`
- Supporting enums:
  `UnaryPosition`, `ObjectCreationKind`

## Lambda expressions

`LambdaExpression` models anonymous functions and closures:
- **Parameter Forms**:
  - Single unparenthesized untyped parameter: `x => expr`
  - Parenthesized untyped parameters: `(x, y) => expr`
  - Parenthesized typed parameters: `(int32 x, int32 y) => expr`
  - Explicit trailing return type: `(int32 x, int32 y) -> int32 => expr`
- **Body Forms**:
  - Expression body: `(...) => expr`
  - Block body: `(...) => { stmt; return expr; }`
- **Explicit Return Invariant in Block Bodies (`MH2016`)**:
  Lambda block bodies strictly require an explicit `return` statement. Trailing expressions without `return` are rejected with `MH2016`.
- **Typing & Contextual Inference (`MH2015`)**:
  - Lambdas assigned to typed variables or passed to functions with known function/delegate signatures contextually infer their parameter types. Untyped parameters without target context emit `MH2015`.
  - Non-capturing lambdas evaluate to function pointers (`Std.Function<TReturn, TParams...>`).
  - Capturing lambdas evaluate to closures/delegates (`Std.Delegate<TReturn, TParams...>`).

## Things worth noticing

- Some names look similar across declarations and expressions, for example generic names. That is intentional; syntax often needs both declaration-side and usage-side forms.
- `ElseExpression` existing as its own node tells you the tree preserves source structure closely rather than collapsing everything into a single if-expression payload.
- Construction syntax is split into several node types instead of one mega-node with many optional fields, which usually makes parser output easier to inspect.
- Object and array creation can carry object-style `with { ... }` clauses, while collection expressions keep trailing collection modifiers such as `with(...)` separate from the literal body.
- The expression tree is syntax-only. Semantic meaning such as type binding, overload selection, or control-flow correctness belongs to later resolution passes.

## Traversal tip

If you are trying to understand how an expression attaches to surrounding statements or declarations, pair this folder with:

- [`syntax-statements.md`](syntax-statements.md)
- [`syntax-declarations.md`](syntax-declarations.md)
