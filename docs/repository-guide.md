# Maho Repository Guide

This directory is the "where do I go next?" map for the repository.

The root [`README.md`](../README.md) is still the right place for build, run, and CLI basics. This guide helps you jump to the subsystem that owns the behavior you are looking at.

## Repository shape

- [`source-tree.md`](source-tree.md): project-level split between the reusable compiler library (`Maho`), the project/build manager (`Miryo`), and the compiler CLI (`MahoCli`).
- [`miryo.md`](miryo.md): project manager and build tool CLI (`miryo`), `.mhpr` project files, and `miryo.config`.
- [`compiler-library.md`](compiler-library.md): map of the core compiler library.
- [`cli.md`](cli.md): CLI control flow for `mahoc`, including argument parsing, file batching, status output, and diagnostic rendering.

## Go here when...

- You want to understand project scaffolding, `.mhpr` configuration, dependencies, or high-level build commands:
  [`miryo.md`](miryo.md)
- You want to see how `./dist/mahoc` compiles source files and formats output:
  [`cli.md`](cli.md)
- You want the public analysis API, phase timers, or the result payload contract:
  [`analysis.md`](analysis.md)
- You want to trace diagnostics from creation to final text/json output:
  [`diagnostics.md`](diagnostics.md)
  then [`cli.md`](cli.md)
- You want to understand debug JSON and how lexer/parser state is serialized:
  [`analysis.md`](analysis.md)
  and [`syntax.md`](syntax.md)
- You want source text loading, line lookup, or span math:
  [`source-text.md`](source-text.md)
- You want the syntax tree layout, without diving into parser internals immediately:
  [`syntax.md`](syntax.md)
- You want the formal lexical grammar, EBNF syntax, precedence table, and current resolution rules:
  [`language-theory.md`](language-theory.md)
- You want the AST categories:
  [`syntax-declarations.md`](syntax-declarations.md),
  [`syntax-expressions.md`](syntax-expressions.md),
  [`syntax-fragments.md`](syntax-fragments.md),
  [`syntax-statements.md`](syntax-statements.md)
- You want the current semantic model and resolution pipeline:
  [`compiler-library.md`](compiler-library.md)

## Current pipeline

The compiler executes a complete front-end pipeline up through declaration resolution:

1. **Build Orchestration (`Miryo` / `miryo`)**:
   - If managing a project, `miryo` parses the `.mhpr` configuration, resolves inter-project dependencies, determines source files, reads `miryo.config`, and spawns `mahoc` with appropriate flags (e.g. `--entry`, `--unsafe`, `--implicit-toplevel`, `--alias`).
2. **Compiler Driver (`MahoCli` / `mahoc`)**:
   - Direct compiler CLI that accepts `.mh` source files and directories (zero awareness of `.mhpr` project files).
   - Resolves input files and invokes `MahoCompiler.CompileFiles(...)` (or `AnalyzeFiles(...)`).
3. **Syntax Phase (Lexing + Parsing)**:
   - Source files are parsed in parallel via `Parallel.For`.
   - `SourceText` indexes line offsets and enables absolute and line/column span tracking.
   - `Lexer` tokenizes source into tokens with trivia.
   - `Parser` constructs strongly-typed `CompilationUnit` ASTs.
   - Phase duration is measured and recorded in `CompilationPhaseTimers.Syntax`.
4. **Semantic Analysis Phase**:
   - `Resolver` runs semantic passes across the unified `SyntaxTree`:
     - `SymbolDiscoveryPass`: discovers namespaces, builds the namespace trie, registers types/functions/variables/properties/aliases, assigns modifier flags (including `TypeFlags.Partial` and `FunctionFlags.Partial`), and constructs lexical scopes.
     - `DeclarationResolutionPass`: binds type references, verifies base type hierarchies, checks cyclic inheritance (`MH1004`), checks duplicate non-partial types (`MH1002`), performs partial type canonical merging and kind consistency, enforces partial function body limits and signature matching (`MH1003`), checks duplicate variables (`MH1005`), duplicate properties (`MH1006`), and ambiguous type references (`MH1001`).
   - Phase duration is measured and recorded in `CompilationPhaseTimers.SemanticAnalysis`.
5. **Lowering & Codegen Phase**:
   - Currently serves as the lowering boundary (throwing `MH9000` until backend code generation is hooked up).
   - Phase duration is tracked under `CompilationPhaseTimers.Lowering`.
6. **Diagnostics & Status Output**:
   - Diagnostics are enriched with primary carets, secondary context spans, notes, and remediation help, then projected into `DiagnosticInfo` payloads.
   - The CLI renders either rich ANSI-colored reports or JSON envelopes for diagnostics.
   - The compiler calculates and returns `analysis.Elapsed` directly, and the CLI prints the status line:
     - `Build succeeded in <time>` (word `succeeded` in green)
     - `Build succeeded with <n> warning(s) in <time>` (word `succeeded` in green, `warning(s)` in yellow)
     - `Build failed with <n> error(s)` (word `error(s)` in red)
     - `Build failed with <n> error(s) and <m> warning(s)` (word `error(s)` in red, `warning(s)` in yellow)

## Reading strategy

- If you are debugging behavior the user can see, start in the CLI docs and follow the call chain inward.
- If you are debugging spans, underlines, or line/column bugs, read `Text` before `Diagnostics` or renderer code.
- If you are changing AST shapes, read the folder guides under `Syntax` before touching parser code; the folders are organized by syntax category, not by parse phase.
- If you are adding new diagnostics, update both the internal diagnostics manager and the renderer logic that supplies tips and output formatting.
