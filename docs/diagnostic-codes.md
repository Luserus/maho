# Maho Diagnostic Code Registry

This document serves as the official **Language & Compiler Diagnostic Code Specification** for Maho.

All compiler diagnostics follow the standardized prefix `MH` followed by a 4-digit code: `MHxxxx`.

---

## Code Range Allocations

| Range | Domain | Description |
| :--- | :--- | :--- |
| **`MH0000` - `MH0099`** | **Internal & Host Errors** | Failures that cannot be reflected in user source code (CLI errors, I/O failures, build system errors, compiler panics, internal resource limits). |
| **`MH0100` - `MH0499`** | **Syntax & Grammar** | Lexer tokenization errors, parser grammar recovery, malformed declarations, statement/expression syntax errors, and macro pattern grammar. |
| **`MH0500` - `MH0999`** | **Semantic & Resolution** | Name resolution, type binding, duplicate symbols, cyclic hierarchies, macro expansion failures, context violations, and type checking. |
| **`MH1000`+** | **Future Extensions** | Sequential overflow range for advanced semantic analysis, borrow checking, or optimization passes. |

---

## 1. Internal & Host Errors (`MH0000` - `MH0099`)

These diagnostics represent system-level issues outside the program's source text:

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0001` | `FileNotFound` | Source file, project file (`.mhpr`), or import path not found on disk. |
| `MH0002` | `InvalidProjectConfiguration` | Malformed `.mhpr` project file or circular project reference graph. |
| `MH0003` | `CommandLineError` | Unrecognized CLI flag, missing arguments, or invalid option values. |
| `MH0004` | `SourceIoError` | Failed to read from or write to disk during compilation. |
| `MH0010` | `CompilerInternalPanic` | Unhandled internal compiler assertion failure or bug. |

---

## 2. Syntax & Grammar Errors (`MH0100` - `MH0499`)

### 2.1 Lexical Diagnostics (`MH0100` - `MH0119`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0100` | `BadToken` | Illegal character or unrecognized token in source text. |
| `MH0101` | `UnterminatedString` | String literal reaching EOF or unescaped newline without a closing quote. |
| `MH0102` | `UnterminatedCharacter` | Character literal reaching EOF without a closing single quote. |
| `MH0103` | `EmptyCharacterLiteral` | Character literal `''` without payload. |
| `MH0104` | `UnterminatedMultiLineComment` | Multi-line comment reaching EOF without closing `*/`. |
| `MH0105` | `InvalidEscapeSequence` | Unrecognized escape sequence in string or character literal. |

### 2.2 Parser & Grammar Recovery (`MH0120` - `MH0159`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0120` | `ExpectedToken` | Expected punctuation or keyword not found at this grammar position. |
| `MH0121` | `ExpectedExpression` | Statement or operand expected a valid expression. |
| `MH0122` | `ExpectedIdentifier` | Declaration, parameter, or reference expected an identifier token. |
| `MH0123` | `ExpectedType` | Declaration, type argument, or cast expected a type syntax node. |
| `MH0124` | `ExpectedBody` | Type, function, or property expected a block body or expression body. |
| `MH0125` | `ExpectedParameter` | Parameter list expected parameter declaration. |
| `MH0126` | `ExpectedGenericParameter` | Generic parameter list expected generic parameter identifier. |
| `MH0127` | `UnexpectedToken` | Token violates language grammar in current context. |
| `MH0128` | `MissingToken` | Synthesized missing token inserted during error recovery. |

### 2.3 Directive & File Structure (`MH0160` - `MH0189`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0160` | `TopLevelPragmaRequired` | Top-level statements used in a file without `#pragma toplevel enable`. |
| `MH0161` | `MultipleTopLevelSources` | Multiple files in a project attempting to declare top-level statements. |
| `MH0162` | `InvalidPragmaDirective` | Malformed `#pragma` directive or unrecognized pragma argument. |

### 2.4 Macro Syntax & Grammar (`MH0200` - `MH0249`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0200` | `InvalidMacroDeclaration` | Malformed `macro $Name` header or missing arm list. |
| `MH0201` | `InvalidMacroPattern` | Malformed parameter pattern in macro arm (e.g. invalid parameter kind). |
| `MH0202` | `InvalidMacroRepetition` | Malformed variadic pack pattern or repetition syntax `$( ... )...`. |
| `MH0203` | `InvalidMacroEscape` | Malformed or mispositioned macro parameter reference `@name`. |
| `MH0230` | `InvalidTokenConcatenation` | Invalid token pasting with `##` operator in macro template. |

---

## 3. Semantic & Resolution Errors (`MH0500` - `MH0999`)

### 3.1 Symbol Resolution & Name Lookup (`MH0500` - `MH0529`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0500` | `UnresolvedTypeReference` | Type name cannot be resolved in visible or imported scopes. |
| `MH0501` | `AmbiguousTypeReference` | Unqualified type matches multiple declarations across imported namespaces. |
| `MH0502` | `UnresolvedIdentifier` | Variable, parameter, or member identifier not found in scope. |
| `MH0503` | `AmbiguousIdentifier` | Identifier matches multiple visible symbols in scope. |
| `MH0504` | `UnresolvedNamespace` | Namespace in `using` directive or qualified access not found. |

### 3.2 Declarations & Scope Conflicts (`MH0530` - `MH0569`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0530` | `DuplicateTypeDeclaration` | Multiple non-partial type declarations sharing name and arity in the same scope. |
| `MH0531` | `ConflictingPartialTypeKinds` | Partial declarations of the same type specify conflicting kinds (e.g., class vs struct). |
| `MH0532` | `DuplicateFunctionDeclaration` | Duplicate function declarations with identical signatures and generic arity. |
| `MH0533` | `MultiplePartialFunctionBodies` | Multiple declarations of a partial function provide an implementation body. |
| `MH0534` | `ConflictingPartialFunctionReturnTypes` | Partial function declarations specify conflicting return types. |
| `MH0535` | `DuplicateVariableDeclaration` | Variable or field already declared in current scope. |
| `MH0536` | `DuplicatePropertyDeclaration` | Property already declared in current type. |
| `MH0537` | `DuplicateAliasDeclaration` | Type alias conflicts with an existing type or alias in scope. |
| `MH0538` | `CyclicTypeHierarchy` | Base type inheritance or alias expansion forms a cyclic loop. |

### 3.3 Macro Expansion & Metaprogramming (`MH0600` - `MH0649`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0600` | `MacroRecursionLimitExceeded` | Macro expansion depth exceeded the configured `MacroRecursionLimit`. |
| `MH0601` | `NoMatchingMacroArm` | Arguments supplied to `$macro(...)` matched none of the macro's pattern arms. |
| `MH0602` | `UnresolvedMacro` | Macro symbol `$name` not found in lexical or imported scopes. |
| `MH0603` | `InvalidMacroContext` | Macro invoked in incompatible context (e.g. statement macro in expression context). |
| `MH0604` | `MacroDependencyCycle` | Mutually recursive macros or static reflection queries deadlocked without progress. |
| `MH0605` | `InvalidVariadicPackExpansion` | Variadic pack `@item...` used outside repetition or array context. |

---

## 4. Sequential Extensions (`MH1000`+)

Reserved for future type checking, control flow analysis, and lifetime/borrow rules as the compiler expands.

---

## 5. Pipeline & Driver Codes (`MH9000` - `MH9001`)

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH9000` | `CompilerPipelineNotImplemented` | Emitted when compilation front-end analysis succeeds but reaches the unfinished lowering / code generation backend. |
| `MH9001` | `BatchFileAnalysisError` | Emitted when an individual file fails batch analysis due to unhandled I/O or internal read errors. |

