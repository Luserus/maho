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

Semantic resolution operates after parsing has transformed all compilation units into a single `SyntaxTree`. It runs through a pipeline of orchestrated resolution passes managed by `Resolver`:

### 2.1 Symbol Discovery Pass (`SymbolDiscoveryPass`)
The discovery pass builds the initial symbol graph and scope hierarchy:
- **Namespaces**: Constructs a hierarchical `NamespaceTrieNode` for qualified and nested namespaces.
- **Types**: Discovers all classes, structs, interfaces, and enums, assigning unique `SymbolHandle` entries and `TypeFlags` (e.g. `TypeFlags.Partial`).
- **Functions & Methods**: Discovers top-level functions, methods within types, local functions, constructors, and accessors. Captures modifiers into `FunctionFlags` (including `FunctionFlags.Partial`).
- **Variables & Properties**: Discovers global variables, member fields, and properties with their respective enclosing scopes.
- **Aliases**: Discovers global and local `using` alias declarations.
- **Scopes**: Establishes parent-linked `Scope` instances for namespaces, types, functions, parameter lists, and nested blocks.
- **Top-Level Statements**: If opted-in via `#pragma toplevel enable` or project configuration, synthesizes a `Main` function scope for top-level code. Variables declared in `global { }` blocks bypass `Main` and become true global variables.

### 2.2 Declaration Resolution Pass (`DeclarationResolutionPass`)
The declaration resolution pass validates and binds declarations across the discovered symbol table:
- **Type Reference Binding**: Resolves type annotations in base clauses, fields, variables, parameters, return types, properties, and generic constraints.
- **Non-Partial Duplicate Types (`MH1002`)**: Verifies that types sharing the same containing scope, name, and generic arity are all declared with the `partial` modifier. Non-partial redeclarations trigger `MH1002`.
- **Partial Types**: Merges matching partial declarations of the same type into a canonical primary symbol and ensures `TypeKind` consistency (e.g. rejecting partial definitions combining `class` and `struct`).
- **Partial Functions (`MH1003`)**:
  - Allows unlimited bodyless partial declarations without warnings or errors (permitting future macro expansions to implement bodies).
  - Enforces that at most one partial declaration includes an implementation body (`Body is not FunctionEmptyBody`), reporting `MH1003` if multiple bodies are encountered.
  - Validates return type consistency across partial declarations.
- **Duplicate Functions (`MH1003`)**: Groups functions by scope and checks parameter type and generic arity equality via `SignaturesMatch(...)`. Reports duplicate function errors when non-partial functions collide.
- **Duplicate Variables & Fields (`MH1005`)**: Detects duplicate global variables in the same namespace and duplicate fields within product types.
- **Duplicate Properties (`MH1006`)**: Detects duplicate properties in the same enclosing type.
- **Cyclic Type Hierarchies (`MH1004`)**: Resolves base types and traverses inheritance graphs via depth-first search, reporting cycles before downstream layout code executes.
- **Ambiguous Type References (`MH1001`)**: Identifies collisions when unqualified type names match multiple imported types from different namespaces.
- **Generic Constraints**: Verifies that type-valued generic arguments satisfy constraints across direct types, base types, and type aliases.

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
 [Semantic Analysis Phase] (measured by CompilationPhaseTimers.SemanticAnalysis)
   └── Resolver (shared DiagnosticsManager)
        ├── SymbolDiscoveryPass (scopes, trie, symbols, flags, global aliases)
        └── DeclarationResolutionPass (type binding, duplicate/partial checks, cycles, constraints)
       │
       ▼
 [Lowering & Codegen Phase] (measured by CompilationPhaseTimers.Lowering)
   └── Backend lowering (placeholder stage throwing MH9000 until implemented)
       │
       ▼
 Compilation / Analysis Result
   └── CompilerProjectAnalysisResult / DebugCompilationOutput
        ├── Diagnostic projections
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
