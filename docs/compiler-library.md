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

Semantic resolution operates after parsing has transformed all compilation units into a single `SyntaxTree`. It executes through an orchestrated pipeline of 9 sequential resolution passes managed by `Resolver`:

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
6. ConstantFoldingPass
       │
       ▼
7. ControlFlowAnalysisPass
       │
       ▼
8. LifetimeAnalysisPass
       │
       ▼
9. VisibilityAnalysisPass
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
  - `[IntrinsicFunction("...")]`: declares intrinsic runtime functions with canonical signature contracts (e.g. `"Int32.LessThanOrEqual(Int32, Int32)"`).
- **Intrinsic Hierarchy & Implicit Deriving**:
  - *ValueType* (`Std.ValType`, `[IntrinsicType("ValueType")]`): root value type for all value representations; has zero base types.
  - *Direct Inheritors of ValueType*: Direct value-kind abstractions explicitly inherit `: ValType` in library source: `Struct`, `Enum`, `Function`, `Delegate`, `Unit`, `Tuple`, `Array`, `FixedArray`, `Metadata`, `Pointer`, `MutablePointer`, `Reference`, `MutableReference`.
  - *Implicit Struct Deriving*: User-defined structs, primitive scalars (`Int8`-`Int128`, `UInt8`-`UInt128`, `Float16`-`Float128`, `Bool`, `Char`), `Str`, and `Span<T>` implicitly inherit `Struct` (and transitively `ValType`) without explicit base declarations.
  - *Implicit Enum Deriving*: User-defined enums and `Optional<T>` implicitly inherit `Enum` (and transitively `ValType`).
  - *Never* (`Never`, `[IntrinsicType("Never")]`): bottom type assignable to any type; has zero base types and implements no constructors or operations.
- **Topological ABI Verification**: Enforces memory layout and field requirements on types marked with `[IntrinsicType]`:
  - *Primitive Scalars* (`Int8`-`Int128`, `UInt8`-`UInt128`, `Float16`-`Float128`, `Bool`, `Char`, `Unit`, `Never`): must have zero instance fields and zero generic parameters (`MH0544`). `Char` represents a 4-byte Unicode scalar value.
  - *Function Pointers* (`Std.Function<TReturn, TParams...>`): exactly 1 instance field: `nuint` code address (`MH0544`).
  - *Delegates* (`Std.Delegate<TReturn, TParams...>`): exactly 2 instance fields: `nuint` code address and `Pointer<ValType>` environment pointer (`MH0544`).
  - *Pointers & References* (`Pointer<T>`, `MutablePointer<T>`, `Reference<T>`, `MutableReference<T>`): require `NUInt` to be declared; must have exactly 1 generic parameter and 1 instance field of type `nuint`.
  - *Str*: requires `Int8`, `Reference`, and `Int64`; must have 2 instance fields (`Int8&` buffer reference and `Int64` byte length).
  - *Array & Span*: verified against their element types and length fields.
- **Lazy Intrinsic Requirement (`MH0240`)**: Intrinsics are lazily required. Minimal or empty compilations do not fail verification; `MH0240` is reported only when a syntactic construct requiring an intrinsic is used without declaration.
- **Operator Function Cataloging (`DiscoveredOperators`)**: Discovers operator methods declared on interfaces with `[OperatorFunction]` (e.g., `Addition`, `Subtraction`, `Multiplication`, `Division`, `Remainder`, `Equality`, `Inequality`, `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual`, `Dereference`, `IndirectMemberAccess`, `AddressOf`, `New`, `Put`, `Callable`, `IndexerGetter`) and registers them in the operator registry.
  - *Receiver APIs*: `Deref<TReturn>` / `Arrow<TReturn>` declare `&this` for immutable access, while `MutDeref<TReturn>` / `MutArrow<TReturn>` declare `mut &this` for mutable access.
  - *AddressOf*: every type is implicitly populated with an `AddressOf(&this) -> T&` operator overload.

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
- **Modifier Validation & Canonical Ordering (`MH0128`, `MH0129`, `MH0130`, `MH0131`)**:
  - Enforces permitted modifiers and canonical ordering per declaration item (`[visibility] [storage/lifetime] [virtual/unsealed] [safety] [partial] [untagged]`).
  - `static` is strictly a lifetime/storage modifier for global variables and type-bound static fields; it is **disallowed on functions and methods** (`MH0128`), where staticness is inferred purely from the absence of a `this` receiver parameter.
  - Global variables declared at file scope **require** the `static` modifier (`MH0128`).
  - Reports out-of-order modifiers (`MH0129`), duplicates (`MH0130`), and conflicts (`MH0131`).
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
- **Universal L-Values & Reference Aliasing**:
  - Any expression returning a mutable alias (`mut &T` or `mut *T`) is recognized as a valid, assignable L-value location.
  - Safe (`&T`) and unsafe (`*T`) reference aliases must be initialized at their declaration site (`MH0125`).
  - Taking the address of an expression `&expr` invokes the `AddressOf` operator on the expression's underlying type.
  - The bottom type `Never` is unconditionally assignable to any target type without requiring an assignment operator implementation.
- **Operator Overload Resolution & Receiver Mutability**:
  - Binary and unary operators resolve strictly to explicit interface implementations on the operand types (e.g. `<Int32 as Addition>::Add`). Emits `MH2001` or `MH2002` if missing.
  - Unary and member operator dispatch enforces receiver mutability: methods declaring `mut &this` or `this mut*` require a mutable operand/receiver (`ReferenceSemanticType.IsMutable`, `PointerSemanticType.IsMutable`, or `AliasSemanticType.IsMutable`).
  - `Deref<&T>` and `Arrow<&T>` provide safe immutable dereferencing; `MutDeref<mut &T>` and `MutArrow<mut &T>` provide mutable dereferencing yielding assignable L-values.
- **Two-Phase Object Allocation & Construction**:
  - Placement allocation (`put<arena> Type(...)`) resolves the `Put` operator.
  - Heap allocation (`new<allocator> Type(...)`) resolves the `New` operator.
  - Target constructor resolution matches `Construct` method on the type implementing the `Constructor` operator interface.
- **Callable Operator (`Callable`)**:
  - Non-function instances can be invoked if the type implements `Callable<TReturn, ...>`, reporting `MH0611` if not callable.
- **Literal Range Overflow Warning (`MH3001`)**:
  - Emits compile-time warning `MH3001` when an integer literal exceeds the range of its explicit target type (e.g. `int8 x = 128;`) without failing expression resolution.
- **Strict Exact Branch Matching (`MH2011`)**:
  - Conditional `if-else` expressions require exact matching between branches. Implicit widening to a common base type or `Object` is rejected with `MH2011`.
- **Sum Types & Pattern Matching**:
  - Exhaustiveness checking ensures all variants/constituents are covered (`MH0605`).
  - Untagged sum types cannot be matched without an explicit discriminant (`MH0606`).
  - Invoking ordinary instance methods on untagged sum types requires the `untagged` modifier (`MH0608`).
  - Direct variant-level method calls on sum type instances are prohibited without pattern matching (`MH0609`).

---

### 2.6 Constant Folding Pass (`ConstantFoldingPass`)
The constant folding pass executes prior to CFG construction:
- **Compile-Time Expression Evaluation**: Recursively evaluates pure operations on literal scalars, booleans, characters, and string constants using arbitrary-precision `BigInteger` for integers and `double` for floats.
- **Short-Circuit Evaluation**: Safely evaluates short-circuiting boolean operations (`&&`, `||`) without raising runtime exceptions (e.g., `false && (1 / 0 == 0)` folds to `false`).
- **Const Variable Propagation**: Propagates compile-time values of `const` local variables, fields, and globals into downstream expressions.
- **CFG Pruning Preparation**: Records folded constants in `ResolutionContext.ConstantValues` for query during CFG branch pruning.

---

### 2.7 Control Flow Analysis Pass (`ControlFlowAnalysisPass`)
The control flow analysis pass constructs control flow graphs, checks reachability, and infers function return types:
- **Basic Blocks & Graph Construction**: Decomposes function bodies into `BasicBlock` sequences connected via `BlockTerminator` edges (`Branch`, `ConditionalBranch`, `Return`, `Unreachable`).
- **Constant Branch Pruning**: Queries pre-evaluated constants from `ConstantFoldingPass` to prune unreachable branch edges (e.g., `if (10 - 10 == 0)` folds to unconditionally true).
- **Dead Code & Reachability Analysis (`MH2017`, `MH2018`)**:
  - Traverses the CFG from the entry block to determine reachable blocks and divergence.
  - Enforces that non-Unit functions return on all reachable paths (`MH2017`).
  - Infers infinite loop divergence and ensures diverging functions declare `Never` return type (`MH2018`).
- **Unconstrained Literal Collapsing**: Collapses remaining unconstrained scalar literals in global variables and fields into default types (`Int32`, `Float64`).

---

### 2.8 Lifetime Analysis Pass (`LifetimeAnalysisPass`)
The lifetime analysis pass enforces move semantics, borrow checking, and ownership tracking:
- **Place & Storage Modeling**: Maps locals, fields, and temporary storage into fine-grained `Place` allocations.
- **Borrow & Move Verification**: Enforces single-writer/multiple-reader aliasing invariants; verifies move validity and detects use-after-move or simultaneous conflicting borrows.

---

### 2.9 Visibility Analysis Pass (`VisibilityAnalysisPass`)
The visibility analysis pass executes after all other semantic passes have completed to enforce access boundaries:
- **Declaration Source of Truth**: Source code declarations are the sole source of truth; intrinsic types follow declaration access rules, interface members inherit the interface's accessibility, and top-level items are restricted to `public` and `internal` (`MH0128`).
- **Access Boundary Enforcement**:
  - `private` (`MH0520`): Accessible only from within the declaring type.
  - `protected` (`MH0521`): Accessible only within the declaring type and its derived types.
  - `internal` (`MH0522`): Accessible across compilation units in the same project, but forbidden across project boundaries.
  - `public`: Universally accessible across projects and modules.
- **Property Accessor Restrictiveness**: Ensures property getters and setters are verified individually against their accessor visibility.
- **Type Accessibility (`MH0523`)**: Verifies that references to nested types adhere to enclosing type visibility rules.

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
        ├── DeclarationResolutionPass (type binding, partial merging, duplicate checks, modifier rules)
        ├── ExpressionResolutionPass (operator overloads, retroactive literal collapsing, sum types)
        ├── ConstantFoldingPass (compile-time expression folding, const propagation)
        ├── ControlFlowAnalysisPass (CFG, constant branch pruning, return/divergence verification)
        ├── LifetimeAnalysisPass (ownership, move semantics, borrow checking)
        └── VisibilityAnalysisPass (access control enforcement across project/type boundaries)
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
