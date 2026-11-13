# Maho Core Compiler Library Guide

`src/Maho` is the reusable compiler library. It contains the source text model, lexer, parser, syntax model, diagnostic engine, semantic resolution pipeline, and public analysis contracts.

---

## 1. Folder Map

- [`source-text.md`](source-text.md) (`Text/`): Source text representation, line indexing, and absolute/location span arithmetic.
- [`syntax.md`](syntax.md) (`Syntax/`): Lexer, parser, and complete AST hierarchy (declarations, expressions, statements, fragments).
- [`diagnostics.md`](diagnostics.md) (`Diagnostics/`): Rich diagnostic model, `DiagnosticBuilder`, labels, hints, and `DiagnosticsManager`.
- [`analysis.md`](analysis.md) (`Analysis/`): Public API surface (`Compilation`, `AnalysisSession`, `MahoCompiler`) and serializable result contracts.
- `Resolution/`: Semantic analysis passes and symbol table architecture.

---

## 2. Semantic Resolution Architecture (`Resolution/`)

Semantic resolution operates after parsing has transformed all compilation units into a single `SyntaxTree`. It executes through an orchestrated pipeline of 6 sequential resolution passes managed by `Resolver`:

```
1. MacroExpansionPass
       │
       ▼
2. SymbolDiscoveryPass
       │
       ▼
3. IntrinsicDiscoveryPass
       │
       ▼
4. DeclarationResolutionPass
       │
       ▼
5. ExpressionResolutionPass
       │
       ▼
6. ControlFlowAndReturnInferencePass
```

---

### 2.1 Macro Expansion Pass (`MacroExpansionPass`)
The macro expansion pass resolves and expands syntactic macros across the AST:
- **Macro Invocations**: Identifies and expands macro invocations (`$macro(...)`) across AST expressions and statements.
- **Hygiene & Scoping**: Tracks token hygiene (`HygieneId`) across expansion steps, ensuring macro-generated identifiers do not unintentionally shadow or collide with enclosing user symbols.
- **Recursion Limits**: Enforces a configurable maximum recursion depth (`MacroRecursionLimit`, defaulting to 128) to prevent infinite macro recursion loops (`MH0600`).
- **Invocation Tracing**: Tracks nested macro invocation chains (`CompilerMacroTrace`) so errors inside expanded code produce rich tree-formatted tracebacks (`└── from this macro invocation`).

---

### 2.2 Symbol Discovery Pass (`SymbolDiscoveryPass`)
The discovery pass builds the initial symbol graph and lexical scope hierarchy:
- **Namespaces**: Constructs a hierarchical `NamespaceTrieNode` for qualified and nested namespaces.
- **Types**: Discovers all classes, structs, interfaces, enums, and sum types, assigning unique `SymbolHandle` entries and `TypeFlags` (e.g. `TypeFlags.Partial`, `TypeFlags.Untagged`).
- **Functions & Methods**: Discovers top-level functions declared with `func`, methods within types, local functions, constructors, and accessors. Captures modifiers into `FunctionFlags` (including `FunctionFlags.Partial`, `FunctionFlags.Literal`, `FunctionFlags.Untagged`).
- **Lambdas & Closures**: Traverses lambda expressions (`LambdaExpression`), registering lexical scopes and parameter symbols for lambda parameters (`LambdaParameterSyntax`) and nested local declarations inside lambda block bodies.
- **Variables & Properties**: Discovers global variables, member fields, and properties with their respective enclosing scopes.
- **Aliases**: Discovers global and local `using` alias declarations and registers global project aliases.
- **Scopes**: Establishes parent-linked `Scope` instances for namespaces, types, functions, lambdas, parameter lists, and nested blocks.
- **Top-Level Statements & Scope Directives**: If opted-in via `#pragma toplevel enable` or project configuration, synthesizes a `Main` function scope for top-level code. Block scope directives:
  - `global { ... }`: Declarations bypass `Main` and become true global/namespace symbols.
  - `const { ... }`: Establishes compile-time evaluation context for inner declarations.
  - Inner declarations declare their own modifiers; modifiers do not propagate from block scopes.

---

### 2.3 Intrinsic Discovery Pass (`IntrinsicDiscoveryPass`)
The intrinsic discovery pass validates compiler intrinsic types and catalogs operator interfaces:
- **Attribute Verification**: Discovers and validates compiler-recognized attribute declarations:
  - `[IntrinsicType("...")]`: must accept exactly one `Str` parameter.
  - `[OperatorFunction("...")]`: must accept one `Str` parameter naming the operator kind.
  - `[IntrinsicFunction("...")]`: declares intrinsic runtime functions.
- **Intrinsic Hierarchy & Implicit Deriving**:
  - *ValueType* (`Std.ValType`, `[IntrinsicType("ValueType")]`): root value type for all value representations.
  - *Struct* (`Std.Struct`, `[IntrinsicType("Struct")]`): derives from `Std.ValType`. All user structs implicitly derive from `Std.Struct` (and transitively `Std.ValType`).
  - *Unit* (`Unit`, `[IntrinsicType("Unit")]`): derives from `Std.ValType` as a compiler-known 0-layout type with zero instance fields.
  - *Function & Delegate* (`Std.Function`, `Std.Delegate`): derive from `Std.ValType`.
- **Topological ABI Verification**: Enforces memory layout and field requirements on types marked with `[IntrinsicType]`:
  - *Primitive Scalars* (`Int8`-`Int64`, `UInt8`-`UInt64`, `Float32`, `Float64`, `Bool`, `Char`, `Void`): must have zero instance fields and zero generic parameters (`MH0544`).
  - *Function Pointers* (`Std.Function<TReturn, TParams...>`): exactly 1 instance field: `nuint` code address (`MH0544`).
  - *Delegates* (`Std.Delegate<TReturn, TParams...>`): exactly 2 instance fields: `nuint` code address and `Pointer<ValType>` environment pointer (`MH0544`).
  - *Pointer & Reference* (`Pointer<T>`, `Reference<T>`): requires `NUInt` to be declared; must have exactly 1 generic parameter and 1 instance field of type `nuint`.
  - *Str*: requires `Int8`, `Reference`, and `Int64`; must have 2 instance fields (`Int8&` buffer reference and `Int64` byte length).
  - *Array & Span*: verified against their element types and length fields.
- **Lazy Intrinsic Requirement (`MH0240`)**: Intrinsics are lazily required. Minimal or empty compilations do not fail verification; `MH0240` is reported only when a syntactic construct requiring an intrinsic is used without declaration.
- **Operator Function Cataloging (`DiscoveredOperators`)**: Discovers operator methods declared on interfaces with `[OperatorFunction]` (e.g., `Add`, `Subtract`, `New`, `Put`, `Callable`, `IndexerGetter`) and registers them in the operator registry.

---

### 2.4 Declaration Resolution Pass (`DeclarationResolutionPass`)
The declaration resolution pass validates and binds declarations across the discovered symbol table:
- **Type Reference Binding**: Resolves type annotations in base clauses, fields, variables, parameters, return types, properties, and generic constraints.
- **Function Pointer & Delegate Types**: Resolves `FunctionTypeSyntax` (`func(...) -> R`) to `Std.Function<TReturn, TParams...>` and `DelegateTypeSyntax` (`delegate(...) -> R`) to `Std.Delegate<TReturn, TParams...>`.
- **Unified Function Typing**: Assigns first-class types to all functions: static and global functions receive `Std.Function`, while instance methods receive `Std.Delegate`. Functions implement `Callable` and can access members on their type directly (`Func.Member`).
- **Non-Partial Duplicate Types (`MH0530`)**: Verifies that types sharing the same containing scope, name, and generic arity are all declared with `partial`.
- **Partial Types & Functions (`MH0531`, `MH0532`, `MH0533`, `MH0534`)**: Merges partial declarations, validates kind consistency, enforces that at most one declaration supplies an implementation body, and matches signatures.
- **Duplicate Variables, Fields & Properties (`MH0535`, `MH0536`)**: Detects collisions in enclosing scopes.
- **Cyclic Type Hierarchies (`MH0538`)**: Detects cyclic inheritance graphs via depth-first search.
- **Ambiguous Type References (`MH0501`)**: Identifies collisions when unqualified type names match multiple imported namespaces.
- **Copy / Move / Destructor Semantics**:
  - `MH0243`: A type cannot implement both `Destructor` and `Copy` (types with destructors must use move semantics).
  - `MH0244`: A type cannot implement both `Copy` and `Move` (a type must be exclusively Copy or exclusively Move).

---

### 2.5 Expression Resolution Pass (`ExpressionResolutionPass`)
The expression resolution pass types AST expressions and enforces Maho's operator and literal resolution semantics:
- **Sequential Eager Collapsing with Retroactive Literal Resolution**:
  - Literals do not possess a fixed compile-time type upfront. Instead, unsuffixed literals evaluate to unconstrained `CompileTimeLiteralType` instances (`Int`, `Float`, `Str`, `Char`).
  - Canonical booleans (`true`, `false`) immediately resolve to canonical `bool`.
  - When an uncollapsed literal encounters a concrete target type (via assignment, variable typing, function argument, or mixed binary operator), the target type eagerly collapses the literal and retroactively propagates down its expression tree, re-resolving operators and binding methods on the target type (e.g., `var x = 10; var y = x + 10.Abs(); Concrete c = y;` collapses `y`, `x`, and `10` to `Concrete` and checks `Concrete.Abs()`).
  - Unconstrained literals left at the end of a block/function scope fall back to default intrinsic types (`Int32`, `Float64`, `String`, `Char`).
- **Zero Frontend Constant Folding**:
  - The compiler frontend does not perform constant folding (e.g. `10 + 20` is not folded to `30`). Operator calls remain as AST operator nodes resolving to operator interface methods (`<T as Add>::Add`), leaving optimization to the backend with full semantic knowledge.
- **Lambda Expressions & Closures**:
  - Non-capturing lambdas evaluate to `Std.Function<TReturn, TParams...>`.
  - Capturing lambdas evaluate to `Std.Delegate<TReturn, TParams...>`.
  - Block bodies strictly require an explicit `return` statement; trailing expressions are rejected with `MH2016`.
  - Bidirectional contextual typing infers untyped parameter types when assigned to typed variables or passed as arguments to functions expecting function/delegate types; un-inferrable parameters emit `MH2015`.
- **Operator Overload Resolution**:
  - Binary and unary operators resolve strictly to explicit interface implementations on the operand types (e.g. `<Int32 as Add>::Add`). Emits `MH2001` or `MH2002` if missing.
- **Two-Phase Object Allocation & Construction**:
  - Placement allocation (`put<arena> Type(...)`) resolves the `Put` operator.
  - Heap allocation (`new<allocator> Type(...)`) resolves the `New` operator.
  - Target constructor resolution matches `Construct` method on the type implementing the `Constructor` operator interface.
- **Callable Operator (`Callable`)**:
  - Non-function instances can be invoked if the type implements `Callable<TReturn, ...>`, reporting `MH0611` if not callable.
- **Strict Exact Branch Matching (`MH2011`)**:
  - Conditional `if-else` expressions require exact matching between branches. Implicit widening to a common base type or `Object` is rejected with `MH2011`.
- **Sum Types & Pattern Matching**:
  - Exhaustiveness checking ensures all variants/constituents are covered (`MH0605`).
  - Untagged sum types cannot be matched without an explicit discriminant (`MH0606`).
  - Invoking ordinary instance methods on untagged sum types requires the `untagged` modifier (`MH0608`).
  - Direct variant-level method calls on sum type instances are prohibited without pattern matching (`MH0609`).

---

### 2.6 Control Flow & Return Inference Pass (`ControlFlowAndReturnInferencePass`)
The control flow and return inference pass constructs control flow graphs, checks reachability, and infers function return types:
- **Basic Blocks & Graph Construction**: Decomposes function bodies into `BasicBlock` sequences connected via `BlockTerminator` edges (`Branch`, `ConditionalBranch`, `Return`, `Unreachable`).
- **Dead Code & Reachability Analysis (`MH2012`)**:
  - Traverses the CFG from the entry block using depth-first search to mark reachable blocks.
  - Reports unreachable blocks containing user statements as dead code (`MH2012`).
  - Analysis is purely diagnostic; no AST dead-code elimination or optimization is performed in the frontend.
- **Return Statement Inference & Conflict Verification**:
  - For functions declared with `auto` or inferred return types, collects return expressions across all branches and infers a consistent return type.
  - Emits `MH2012` if conflicting return types are returned across different branches.
  - Verifies that non-void functions have an exhaustive return path reaching exit.

---

## 3. Runtime Flow Inside the Library

```
Source Files (.mh)
       │
       ▼
 [Syntax Phase] (measured by CompilationPhaseTimers.Syntax)
   ├── SourceText (line indexing & UTF-8 span tracking)
   ├── Parallel.For
   │    ├── Lexer (tokens with trivia)
   │    └── Parser (recursive descent + Pratt precedence climbing)
   └── SyntaxTree (unified compilation units & AST)
       │
       ▼
 [Macro Expansion Phase]
   └── MacroExpansionPass (macro expansion & invocation trace tracking)
       │
       ▼
 [Semantic Analysis Phase] (measured by CompilationPhaseTimers.SemanticAnalysis)
   └── Resolver (shared DiagnosticsManager)
        ├── SymbolDiscoveryPass (scopes, trie, symbols, flags, global aliases)
        ├── IntrinsicDiscoveryPass (intrinsic types, ABI layouts, operator catalog)
        ├── DeclarationResolutionPass (type binding, partial merging, duplicate checks, cycles)
        ├── ExpressionResolutionPass (operator overloads, retroactive literal collapsing, sum types)
        └── ControlFlowAndReturnInferencePass (CFG, dead code analysis, return type inference)
       │
       ▼
 [Lowering & Codegen Phase] (measured by CompilationPhaseTimers.Lowering)
   └── Backend lowering (placeholder stage throwing MH9000 until implemented)
       │
       ▼
 Compilation
   └── Compilation
        ├── Diagnostics & GetDiagnostics(filePath)
        ├── SyntaxTrees / Files (per-file syntax trees and diagnostics)
        ├── CompilationPhaseTimers (Syntax, SemanticAnalysis, Lowering, Total)
        └── Elapsed (total core compilation time)
```

---

## 4. Subsystem Guides

- For project orchestration and `.mhpr` builds: [`miryo.md`](miryo.md)
- For the compiler driver CLI (`mahoc`): [`cli.md`](cli.md)
- For public compilation APIs and phase timers: [`analysis.md`](analysis.md)
- For span arithmetic and line/column projection: [`source-text.md`](source-text.md)
- For diagnostic creation and error codes: [`diagnostics.md`](diagnostics.md)
- For formal syntax grammar and AST definitions: [`language-theory.md`](language-theory.md)
- For repository project layout: [`source-tree.md`](source-tree.md)
