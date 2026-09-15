# Syntax Declarations Guide

The `Declarations` folder contains syntax nodes for names, types, parameters, variables, and declaration forms across different grammar contexts.

This folder is more about shape than behavior. The parser builds these nodes elsewhere; the files here define what the syntax tree can represent.

## Main groups in this folder

### Name syntax

- `SimpleName`
- `QualifiedName`
- `GenericName`
- `NamedSyntax`

These model identifier-like structures and name composition.

### Type syntax

- `TypeSyntax`
- `SimpleType`
- `QualifiedType`
- `GenericType`
- `ModifiedType`
- `TypeKind`

These files define how type references are represented independently of semantic meaning.

### Declaration families

- `NamespaceDeclaration`
- `TypeDeclaration`
- `FunctionDeclaration`
- `VariableDeclaration`
- `AmbiguousPointerDeclaration`
- `AmbiguousReferenceDeclaration`
- `Parameter`

These are the shared declaration nouns.

### Context-specific declaration wrappers

- `TopLevelDeclaration`
- `TopLevelTypeDeclaration`
- `TopLevelFunctionDeclaration`
- `MemberTypeDeclaration`
- `MemberFunctionDeclaration`
- `MemberVariableDeclaration`
- `LocalDeclaration`
- `LocalTypeDeclaration`
- `LocalFunctionDeclaration`

The repeated top-level/member/local variants are worth noting. They preserve grammatical context explicitly instead of storing one declaration node plus a context flag, which keeps later semantic work from re-deriving placement rules.

## How to traverse this folder

- Start with `NamedSyntax` and `TypeSyntax` if you want the shared abstractions.
- Read the context-specific wrappers if you are trying to understand where a declaration can legally appear.
- Jump to [`../Fragments/docs/README.md`](../../Fragments/docs/README.md) when the declaration points at bodies, modifiers, or declarator pieces.
