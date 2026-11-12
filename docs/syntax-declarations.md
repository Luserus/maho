# Syntax Declarations Guide

The `Declarations` folder contains syntax nodes for names, types, parameters, variables, and declaration forms across different grammar contexts.

This folder is more about shape than behavior. The parser builds these nodes elsewhere; the files here define what the syntax tree can represent.

## Main groups in this folder

### Name syntax

- `SimpleName`
- `QualifiedName`
- `GenericName`
- `TupleName`
- `NamedSyntax`

These model identifier-like structures and name composition.

### Type syntax

- `TypeSyntax`
- `SimpleType`
- `QualifiedType`
- `GenericType`
- `ModifiedType`
- `TupleType`
- `TupleTypeElement`
- `UniformTupleType`
- `FunctionTypeSyntax`
- `DelegateTypeSyntax`
- `TypeKind`

These files define how type references are represented independently of semantic meaning. `FunctionTypeSyntax` (`func(params) -> ReturnType`) and `DelegateTypeSyntax` (`delegate(params) -> ReturnType`) represent function pointer and delegate types respectively.

### Declaration families

- `NamespaceDeclaration`
- `TypeDeclaration`
- `FunctionDeclaration`
- `VariableDeclaration`
- `Parameter`

These are the shared declaration nouns.

#### Function Declarations
Function declarations in Maho use the `func` keyword with trailing return types:
```text
[premodifiers] func identifier<TParams>(params) [postmodifiers] [-> ReturnType] [postmodifiersAndclauses] { body }
```
- **Trailing Return Type**: The return type appears after the parameter list prefixed by `->` (e.g. `func Add(int32 a, int32 b) -> int32 { return a + b; }`).
- **Default Return Type**: If `-> ReturnType` is omitted, the return type defaults to `void`.
- **Pre- and Post-Modifiers**: Modifiers (such as `public`, `static`, `const`, `mut`, `untagged`) are permitted both before `func` and after the parameter list / return type; semantic analysis validates their applicability.
- **Constraints**: Generic `where` constraint clauses appear after the trailing return type.
- **Bodies**: Functions support block bodies (`{ ... }`), expression bodies (`=> expr;`), or empty declarations (`;`).
- **Unified Function Typing**: All function declarations receive a first-class function type (`Std.Function<TReturn, TParams...>` for static/global functions, `Std.Delegate<TReturn, TParams...>` for instance methods). They implement the `Callable` interface and can access members directly from their type (`Func.Member`).

#### Function Pointer and Delegate Types
Function pointers and delegates are declared with trailing return types:
- `modifiers func(params) -> ReturnType identifier;` defines a function pointer matching exact static/global function signatures (aliases intrinsic `Std.Function<TReturn, TParams...> : Callable`).
- `modifiers delegate(params) -> ReturnType identifier;` defines a delegate with an implicit environment pointer subject to lifetime rules (aliases intrinsic `Std.Delegate<TReturn, TParams...> : Callable`).
Variable declarations remain `type ident;`, giving clean, unambiguous decoration where the function/delegate type is the variable's type.

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
- Jump to [`syntax-fragments.md`](syntax-fragments.md) when the declaration points at bodies, modifiers, or declarator pieces.
