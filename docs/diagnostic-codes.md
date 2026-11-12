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

### 2.4 Macro Syntax & Grammar (`MH0200` - `MH0239`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0200` | `InvalidMacroDeclaration` | Malformed `macro $Name` header or missing arm list. |
| `MH0201` | `InvalidMacroPattern` | Malformed parameter pattern in macro arm (e.g. invalid parameter kind). |
| `MH0202` | `InvalidMacroRepetition` | Malformed variadic pack pattern or repetition syntax `$( ... )...`. |
| `MH0203` | `InvalidMacroEscape` | Malformed or mispositioned macro parameter reference `@name`. |
| `MH0230` | `InvalidTokenConcatenation` | Invalid token pasting with `##` operator in macro template. |

### 2.5 Semantic Syntax & ABI Requirements (`MH0240` - `MH0249`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0240` | `MissingIntrinsicForSyntax` | Syntax requires an intrinsic type that was not declared or failed verification. |
| `MH0242` | `InvalidOperatorFunctionSignature` | Invalid signature for operator function declaration. |
| `MH0243` | `DestructorNandCopy` | Type cannot implement both `Destructor` and `Copy`; types with destructors must use move semantics. |
| `MH0244` | `CopyNandMove` | Type cannot implement both `Copy` and `Move`; a type must be exclusively Copy or exclusively Move. |

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

### 3.2 Declarations & Scope Conflicts (`MH0530` - `MH0539`)
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

### 3.3 Compiler Intrinsics & ABI Layout (`MH0540` - `MH0549`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0540` | `UnknownIntrinsicAttribute` | Unrecognized intrinsic attribute name declared with the `intrinsic` modifier. |
| `MH0541` | `InvalidIntrinsicAttributeSignature` | Invalid parameter or return signature for an intrinsic attribute declaration. |
| `MH0542` | `UnknownIntrinsicType` | Unknown intrinsic type literal specified in `[IntrinsicType("...")]`. |
| `MH0543` | `DuplicateIntrinsicType` | Intrinsic type declared more than once across compilation and referenced libraries. |
| `MH0544` | `IntrinsicAbiMismatch` | Type fails to satisfy the required memory layout or field ABI contract for its intrinsic type. |

### 3.4 Macro Expansion & Metaprogramming (`MH0600` - `MH0604`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0600` | `MacroRecursionLimitExceeded` | Macro expansion depth exceeded the configured `MacroRecursionLimit`. |
| `MH0601` | `NoMatchingMacroArm` | Arguments supplied to `$macro(...)` matched none of the macro's pattern arms. |
| `MH0602` | `UnresolvedMacro` | Macro symbol `$name` not found in lexical or imported scopes. |
| `MH0603` | `InvalidMacroContext` | Macro invoked in incompatible context (e.g. statement macro in expression context). |
| `MH0604` | `MacroDependencyCycle` | Mutually recursive macros or static reflection queries deadlocked without progress. |

### 3.5 Sum Types, Patterns & Invocations (`MH0605` - `MH0619`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0605` | `NonExhaustiveMatch` | Match expression is not exhaustive and fails to cover all variants or constituents. |
| `MH0606` | `UntaggedMatchDisallowed` | Match expression attempted on an untagged sum type without an explicit discriminant. |
| `MH0607` | `InvalidPatternForSumType` | Pattern arm does not match any valid variant or constituent of the sum type. |
| `MH0608` | `UntaggedMethodMismatch` | Invocations of ordinary instance methods on an untagged sum type without the `untagged` modifier. |
| `MH0609` | `DirectVariantMethodInvocation` | Direct invocation of a variant-level method on a sum type instance without pattern unwrapping. |
| `MH0610` | `UntaggedDefinitionTaggedUsage` | Untagged-defined type used in a tagged context without the `untagged` modifier. |
| `MH0611` | `TypeNotCallable` | Expression type is not callable and does not implement the `Callable` operator interface. |

---

## 4. Expression Typing & Control Flow Analysis (`MH2000` - `MH2099`)

These diagnostics are emitted by Pass 5 (`ExpressionResolutionPass`) and Pass 6 (`ControlFlowAndReturnInferencePass`):

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH2001` | `BinaryOperatorTypeMismatch` | Binary operator cannot be applied to left and right operand types (no matching operator interface). |
| `MH2002` | `UnaryOperatorTypeMismatch` | Unary operator cannot be applied to operand type. |
| `MH2003` | `AssignmentTypeMismatch` | Expression type cannot be assigned to target variable type (no implicit conversion). |
| `MH2004` | `ArgumentTypeMismatch` | Argument type cannot be converted to parameter type. |
| `MH2005` | `ArgumentCountMismatch` | Invocation provides fewer or more arguments than expected by parameter signature. |
| `MH2006` | `ReturnTypeMismatch` | Value returned by return statement does not match declared function return type. |
| `MH2007` | `ConditionTypeMismatch` | Condition expression in `if` or `while` does not evaluate to canonical boolean. |
| `MH2008` | `CannotInferVariableType` | Type of inferred variable (`var x`) cannot be determined from initializer. |
| `MH2009` | `CannotAssignToNonLValue` | Left-hand side of assignment must be a variable, field, property or indexer. |
| `MH2010` | `TupleDestructuringError` | Tuple destructuring count or element type mismatch. |
| `MH2011` | `AmbiguousConditionalBranches` | Conditional `if-else` branches evaluate to conflicting types (exact matching required; no implicit widening). |
| `MH2012` | `ConflictingReturnType` | Return statement returns a type that conflicts with a previous return type in the same function. |
| `MH2013` | `AmbiguousLiteralConversion` | Multiple custom literal conversion methods match the literal; explicit suffix is required. |
| `MH2014` | `MemberNotFoundOnType` | Target type does not contain a definition for member during retroactive literal collapse or member lookup. |
| `MH2015` | `CannotInferLambdaParameterType` | Cannot infer type for lambda parameter; explicit type annotation is required. |
| `MH2016` | `LambdaBlockBodyRequiresReturn` | Lambda block body must end with an explicit return statement; trailing expressions are not permitted. |

---

## 5. Sequential Extensions (`MH1000` - `MH1999`)

Reserved for future borrow checking, lifetime analysis, and advanced semantic verification passes.

---

## 5. Pipeline & Driver Codes (`MH9000` - `MH9001`)

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH9000` | `CompilerPipelineNotImplemented` | Emitted when compilation front-end analysis succeeds but reaches the unfinished lowering / code generation backend. |
| `MH9001` | `BatchFileAnalysisError` | Emitted when an individual file fails batch analysis due to unhandled I/O or internal read errors. |

