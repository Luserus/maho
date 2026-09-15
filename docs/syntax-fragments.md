# Syntax Fragments Guide

The `Fragments` folder holds reusable syntax pieces that are smaller than full declarations or statements but still important enough to deserve their own nodes.

This is the folder to read when a declaration or statement feels like it is "made out of pieces" rather than represented as one monolithic node.

## Major fragment groups

### Bodies

- `NamespaceBody`
- `NamespaceBlockBody`
- `NamespaceEmptyBody`
- `TypeBody`
- `TypeBlockBody`
- `TypeEmptyBody`
- `TypeEnumBody`
- `FunctionBody`
- `FunctionBlockBody`
- `FunctionEmptyBody`
- `FunctionLambdaBody`

These types separate the existence of a declaration from the shape of its body.

### Type modifiers

- `PostfixTypeModifier`
- `ArrayTypeModifier`
- `ReferenceTypeModifier`
- `PointerTypeModifier`
- `OptionalTypeModifier`
- `PostfixTypeModifierKind`

This cluster models type-shape decoration without forcing every type node to inline every modifier case.

### Declarator pieces

- `ParameterVariableDeclarator`
- `AssignmentClause`
- `FunctionSignature`
- `ObjectWithClause`
- `CollectionExpressionModifier`
- `CollectionConstructorModifier`

These are the grammar joints that declarations commonly compose.

## Why this folder matters

If the syntax tree feels unusually explicit, this folder is part of the reason. Instead of collapsing syntax sugar too early, the tree keeps many structural distinctions visible.

That is usually helpful for:

- debug printing,
- future formatting tools,
- precise diagnostics,
- and later semantic passes that want to know exactly which syntax form appeared.

It also keeps the parser output stable when body shapes or modifier combinations grow new variants, because those variants can get their own nodes instead of being squeezed into optional fields on an existing type.

## Traversal tip

When a declaration node references a body, modifier, or declarator type you do not recognize, check this folder before assuming it is hidden inside parser logic.
