# Syntax Expressions Guide

The `Expressions` folder contains the AST node shapes for expression syntax.

These files are intentionally declarative: they define the tree that the parser produces, not the parser logic that recognizes the grammar.

## Common anchors

- `Expression.cs`: base type for all expression nodes.
- `NamedExpression.cs`: shared base for expression forms tied to names.

## Expression families in this folder

- Name/reference forms:
  `IdentifierNameExpression`, `GenericNameExpression`
- Literal/grouping forms:
  `LiteralExpression`, `ParenthesizedExpression`
- Operator forms:
  `UnaryExpression`, `BinaryExpression`, `AssignmentExpression`, `CastExpression`
- Access/call forms:
  `MemberAccessExpression`, `CallExpression`, `IndexExpression`
- Control-flow-like forms:
  `IfExpression`, `ElseExpression`, `BlockExpression`
- Construction forms:
  `ArrayCreationExpression`, `ObjectCreationExpression`, `ConstructorCallExpression`, `CollectionExpression`
- Supporting enums:
  `UnaryPosition`, `ObjectCreationKind`

## Things worth noticing

- Some names look similar across declarations and expressions, for example generic names. That is intentional; syntax often needs both declaration-side and usage-side forms.
- `ElseExpression` existing as its own node tells you the tree preserves source structure closely rather than collapsing everything into a single if-expression payload.
- Construction syntax is split into several node types instead of one mega-node with many optional fields, which usually makes parser output easier to inspect.
- The expression tree is syntax-only. Semantic meaning such as type binding, overload selection, or control-flow correctness belongs to later resolution passes.

## Traversal tip

If you are trying to understand how an expression attaches to surrounding statements or declarations, pair this folder with:

- [`../Statements/docs/README.md`](../../Statements/docs/README.md)
- [`../Declarations/docs/README.md`](../../Declarations/docs/README.md)
