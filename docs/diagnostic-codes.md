# Maho Diagnostic Code Registry

This document serves as the official **Language & Compiler Diagnostic Code Specification** for Maho.

All compiler diagnostics follow the standardized prefix `MH` followed by a 4-digit code: `MHxxxx`.

---

## Code Range Allocations

| Range | Domain | Description |
| :--- | :--- | :--- |
| **`MH0000` - `MH0099`** | **Internal & ABI Contracts** | Host system failures, CLI errors, I/O failures, compiler panics, internal resource limits, and **intrinsic/ABI layout contract verification**. |
| **`MH0100` - `MH0499`** | **Syntax & Grammar** | Pure lexer tokenization errors, parser grammar recovery, malformed declarations, statement/expression syntax errors, and macro pattern grammar. |
| **`MH0500` - `MH0999`** | **Semantic & Resolution** | Name resolution, type binding, duplicate symbols, cyclic hierarchies, interface conformance, sum types, match patterns, operator signatures, and accessibility/visibility. |
| **`MH1000` - `MH1499`** | **Ownership & CFG** | Definite initialization, move-after-move, borrow conflicts, return reference to local, copy move-only via alias, partial moves, and CFG reachability/returns. |
| **`MH1500` - `MH1999`** | **Constant & Compile-Time Evaluation** | Constant expression evaluation failures, compile-time literal errors, compile-time integer overflow, and compile-time division by zero in `const` declarations. |
| **`MH2000` - `MH2999`** | **Expression Typing & Invocations** | Binary/unary operator type mismatches, assignment mismatch, argument type/count mismatches, return type mismatch, non-L-value receivers for `&this`/`mut &this`, and non-callable invocations. |
| **`MH3000` - `MH3999`** | **Compiler Warnings** | Non-fatal diagnostics: literal integer overflow warnings (`MH3001`), division by zero detected in runtime expressions (`MH3002`). |
| **`MH9000` - `MH9001`** | **Pipeline & Driver** | Compiler pipeline unreached lowering and batch driver errors. |

---

## 1. Internal & ABI Contracts (`MH0000` - `MH0099`)

These diagnostics represent system-level issues or fundamental compiler/host ABI contracts:

### 1.1 Host & Environment (`MH0001` - `MH0019`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0001` | `FileNotFound` | Source file, project file (`.mhpr`), or import path not found on disk. |
| `MH0002` | `InvalidProjectConfiguration` | Malformed `.mhpr` project file or circular project reference graph. |
| `MH0003` | `CommandLineError` | Unrecognized CLI flag, missing arguments, or invalid option values. |
| `MH0004` | `SourceIoError` | Failed to read from or write to disk during compilation. |
| `MH0010` | `CompilerInternalPanic` | Unhandled internal compiler assertion failure or bug. |

### 1.2 ABI Contracts & Intrinsic Requirements (`MH0020` - `MH0039`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0020` | `IntrinsicAbiMismatch` | Type fails to satisfy the required memory layout or field ABI contract for its intrinsic type. |
| `MH0021` | `MissingIntrinsicForSyntax` | Syntax requires an intrinsic type or operator interface that was not declared or failed verification. |

---

## 2. Syntax & Grammar Errors (`MH0100` - `MH0499`)

Strictly errors in the grammar, lexer, parser, directives, and macro syntax.

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
| `MH0125` | `UninitializedAliasVariable` | Alias variable (`&T` or `*T`) must be initialized at its declaration site. |
| `MH0126` | `ExpectedGenericParameter` | Generic parameter list expected generic parameter identifier. |
| `MH0127` | `ExpectedPattern` | Expected a valid pattern syntax node. |
| `MH0128` | `InvalidModifier` | Modifier is invalid for declaration item, or missing required modifier (e.g., globals require `static`, functions disallow `static`). |
| `MH0129` | `ModifierOutOfOrder` | Modifier is out of canonical order on declaration item. |
| `MH0130` | `DuplicateModifier` | Duplicate modifier specified on declaration. |
| `MH0131` | `ConflictingModifiers` | Conflicting modifiers specified on declaration. |

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

---

## 3. Semantic & Resolution Errors (`MH0500` - `MH0999`)

### 3.1 Symbol Resolution & Accessibility (`MH0500` - `MH0529`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0500` | `UnresolvedTypeReference` | Type name cannot be resolved in visible or imported scopes. |
| `MH0501` | `AmbiguousTypeReference` | Unqualified type matches multiple declarations across imported namespaces. |
| `MH0502` | `UnresolvedIdentifier` | Variable, parameter, or member identifier not found in scope. |
| `MH0503` | `AmbiguousIdentifier` | Identifier matches multiple visible symbols in scope. |
| `MH0504` | `UnresolvedNamespace` | Namespace in `using` directive or qualified access not found. |
| `MH0520` | `PrivateMemberInaccessible` | Private member cannot be accessed outside its declaring type. |
| `MH0521` | `ProtectedMemberInaccessible` | Protected member cannot be accessed outside declaring or derived types. |
| `MH0522` | `InternalMemberInaccessible` | Internal member cannot be accessed from an external project. |
| `MH0523` | `TypeInaccessible` | Type cannot be accessed due to its protection level. |

### 3.2 Declarations & Scope Conflicts (`MH0530` - `MH0549`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0530` | `DuplicateTypeDeclaration` | Multiple non-partial type declarations sharing name and arity in the same scope. |
| `MH0531` | `ConflictingPartialTypeKinds` | Partial declarations of the same type specify conflicting kinds (e.g., struct vs enum). |
| `MH0532` | `DuplicateFunctionDeclaration` | Duplicate function declarations with identical signatures and generic arity. |
| `MH0533` | `MultiplePartialFunctionBodies` | Multiple declarations of a partial function provide an implementation body. |
| `MH0534` | `ConflictingPartialFunctionReturnTypes` | Partial function declarations specify conflicting return types. |
| `MH0535` | `DuplicateVariableDeclaration` | Variable or field already declared in current scope. |
| `MH0536` | `DuplicatePropertyDeclaration` | Property already declared in current type. |
| `MH0537` | `DuplicateAliasDeclaration` | Type alias conflicts with an existing type or alias in scope. |
| `MH0538` | `CyclicTypeHierarchy` | Base type inheritance or alias expansion forms a cyclic loop. |
| `MH0539` | `InconsistentInheritanceOrder` | Inconsistent or cyclic C3 linearization order detected across multiple base types. |
| `MH0540` | `UnknownIntrinsicAttribute` | Unrecognized intrinsic attribute name declared with the `intrinsic` modifier. |
| `MH0541` | `InvalidIntrinsicAttributeSignature` | Invalid parameter or return signature for an intrinsic attribute declaration. |
| `MH0542` | `UnknownIntrinsicType` | Unknown intrinsic type literal specified in `[IntrinsicType("...")]`. |
| `MH0543` | `DuplicateIntrinsicType` | Intrinsic type declared more than once across compilation and referenced libraries. |

### 3.3 Interface Conformance, Inheritance, Layout & Member Access (`MH0550` - `MH0583`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0550` | `UnimplementedInterfaceMember` | Concrete type does not implement a required abstract interface method or property. |
| `MH0551` | `BaseConstructorRequiresExplicitInit` | Base type has no default constructor; derived constructor must explicitly initialize it. |
| `MH0552` | `InvalidBaseInitializerOnOrdinaryFunction` | Base initializer clause `: Base(...)` can only be placed on a constructor. |
| `MH0553` | `TypeIsNotBaseOf` | Type specified in base initializer clause is not an inherited base of the enclosing type. |
| `MH0554` | `DuplicateBaseInitializer` | Base type is initialized more than once in a constructor base initializer list. |
| `MH0555` | `NonContiguousBaseConversion` | Cannot convert derived reference to non-contiguous/split base; use `dyn Base&` instead. |
| `MH0556` | `AmbiguousMemberAccess` | Ambiguous member access across multiple sibling base types; disambiguate via `(expr as Base).Member`. |
| `MH0560` | `InvalidOperatorFunctionSignature` | Invalid signature for operator function declaration. |
| `MH0561` | `DestructorNandCopy` | Type cannot implement both `Destructor` and `Copy`; types with destructors must use move semantics. |
| `MH0562` | `CopyNandMove` | Type cannot implement both `Copy` and `Move`; a type must be exclusively Copy or exclusively Move. |
| `MH0563` | `DuplicateOperatorFunction` | Duplicate `[OperatorFunction]` declaration for the same operator across interfaces. |
| `MH0565` | `ConflictingInterfaceConformance` | Conflicting or duplicate interface conformances across visibility levels, definitions, or extensions. |
| `MH0566` | `InaccessibleInterfaceConformance` | Scoped (`private` or `internal`) interface conformance is inaccessible in the referencing context. |
| `MH0567` | `AmbiguousInterfaceConformance` | Ambiguous interface conformance across multiple imported modules; disambiguate via module qualifier. |
| `MH0568` | `InconsistentCollectionTermCount` | Inconsistent term count across elements in a collection expression. |
| `MH0569` | `MissingCollectionWithContract` | Collection type does not implement the `CollectionWith` contract required for `with(...)` initialization. |
| `MH0570` | `MissingCollectionContract` | Target type does not implement the `Collection` contract for the given element term count. |
| `MH0571` | `TypeNotIterable` | Expression in a `for-in` loop does not implement `Iterable`. |
| `MH0572` | `InvalidYieldContext` | `yield` used outside a generator returning an `Iterator` or `Iterable` type. |
| `MH0573` | `YieldTypeMismatch` | Yielded expression type does not match the iterator item type. |
| `MH0574` | `StructCannotBePiecewiseInitialized` | Struct contains inaccessible fields and cannot be piecewise initialized. |
| `MH0575` | `AmbiguousModuleMember` | Module-qualified reference `Mod::Member` matches multiple imported modules of the same name. |
| `MH0576` | `UndefinedExternModuleAlias` | `extern using` alias references an undefined compiler module alias. |
| `MH0578` | `DirectInitializerFieldError` | Direct struct initializer (`put T { ... }`) is missing a required field or initializes a field more than once. |
| `MH0580` | `UnsizedTypeNotAllowed` | Unsized (DST) type used in a position requiring a statically sized (`Sized`) type. |
| `MH0581` | `GenericConstraintNotSized` | Type argument does not satisfy the `Sized` or `Unsized` constraint on a generic parameter or `Self` bound. |
| `MH0582` | `DerivedTypeAddsFieldsAfterUnsizedBase` | Derived type inherits multiple unsized bases or declares additional instance fields after an unsized base. |
| `MH0583` | `InvalidInterfaceOrDynUsage` | Bare interface used outside constraint/conformance/`dyn`, or invalid `&dyn T` / `mut &dyn T` / `*dyn T` / `(dyn T)&` ordering. |

### 3.4 Macro Expansion & Metaprogramming (`MH0600` - `MH0604`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH0600` | `MacroRecursionLimitExceeded` | Macro expansion depth exceeded the configured `MacroRecursionLimit`. |
| `MH0601` | `NoMatchingMacroArm` | Arguments supplied to `$macro(...)` matched none of the macro's pattern arms. |
| `MH0602` | `UnresolvedMacro` | Macro symbol `$name` not found in lexical or imported scopes. |
| `MH0603` | `InvalidMacroContext` | Macro invoked in incompatible context (e.g. statement macro in expression context). |
| `MH0604` | `MacroDependencyCycle` | Mutually recursive macros or static reflection queries deadlocked without progress. |

### 3.5 Sum Types, Patterns & Invocations (`MH0605` - `MH0620`)
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

## 4. Ownership & Control Flow Analysis (`MH1000` - `MH1499`)

These diagnostics are emitted by `LifetimeAnalysisPass` (move, borrow, partial-move, and drop analyses) and `ControlFlowAnalysisPass`:

### 4.1 Ownership, Borrowing & Lifecycles (`MH1000` - `MH1009`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH1001` | `UseOfMovedValue` | Use of moved value or partially moved field. |
| `MH1002` | `UseOfUninitializedValue` | Use of uninitialized or possibly uninitialized variable or field. |
| `MH1003` | `BorrowConflict` | Cannot borrow or mutate value because an existing conflicting loan is active. |
| `MH1004` | `ReturnReferenceToLocal` | Cannot return a reference to a local variable or temporary. |
| `MH1005` | `CopyMoveOnlyThroughAlias` | Cannot copy a move-only value through a safe alias or reference. |
| `MH1006` | `PartialMoveDisallowed` | Cannot move a field out of a type that implements explicit `Move` or `Destructor`. |
| `MH1007` | `CannotMovePartiallyMoved` | Cannot move partially moved struct; all fields must be initialized to move whole value. |

### 4.2 Control Flow Graph & Returns (`MH1010` - `MH1029`)
| Code | Name | Description |
| :--- | :--- | :--- |
| `MH1010` | `MissingReturn` | Not all reachable code paths return a value in a non-Unit returning function. |
| `MH1011` | `FunctionMustReturnNever` | Function diverges on all code paths and must be declared with return type `Never`. |

---

## 5. Constant & Compile-Time Evaluation Errors (`MH1500` - `MH1999`)

These diagnostics are emitted when evaluating compile-time constants and `const` declarations in `ConstantFoldingPass`:

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH1500` | `ConstDivisionByZero` | Attempted to divide or take remainder by zero in a `const` declaration or constant expression. |
| `MH1501` | `InvalidConstExpression` | Initializer for `const` declaration cannot be resolved to a compile-time constant. |
| `MH1502` | `ConstOverflow` | Compile-time integer arithmetic overflow encountered in `const` expression evaluation. |

---

## 6. Expression Typing & Invocations (`MH2000` - `MH2999`)

These diagnostics are emitted by `ExpressionResolutionPass`:

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
| `MH2019` | `CannotAccessInstanceMemberOnType` | Non-static field, method, or property cannot be accessed through a type name without an instance reference. |
| `MH2020` | `CannotAccessStaticMemberOnInstance` | Static member cannot be accessed via an instance reference; qualify it with the type name instead. |
| `MH2027` | `InvalidReceiverForByRefThis` | Receiver expression must be an addressable L-value for `this` passed by reference (`&this` or `mut &this`). |

---

## 7. Compiler Warnings (`MH3000` - `MH3999`)

Non-fatal compile-time diagnostic warnings:

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH3001` | `LiteralIntegerOverflowWarning` | Literal integer constant value overflows the range of its destination type. |
| `MH3002` | `DivisionByZeroWarning` | Division by zero detected in non-const expression; constant folding declined, deferred to runtime. |

---

## 8. Pipeline & Driver Codes (`MH9000` - `MH9001`)

| Code | Name | Description |
| :--- | :--- | :--- |
| `MH9000` | `CompilerPipelineNotImplemented` | Emitted when compilation front-end analysis succeeds but reaches the unfinished lowering / code generation backend. |
| `MH9001` | `BatchFileAnalysisError` | Emitted when an individual file fails batch analysis due to unhandled I/O or internal read errors. |
