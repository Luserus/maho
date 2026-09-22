# Maho Repository Guide

This directory is the "where do I go next?" map for the repository.

The root [`README.md`](../README.md) is still the right place for build, run, and CLI basics. This guide helps you jump to the subsystem that owns the behavior you are looking at.

## Repository shape

- [`source-tree.md`](source-tree.md): project-level split between the reusable compiler library and the CLI.
- [`compiler-library.md`](compiler-library.md): map of the core library.
- [`cli.md`](cli.md): CLI control flow, including argument parsing, file batching, status output, and rendering.

## Go here when...

- You want to see how `./dist/mahoc` becomes actual work:
  [`cli.md`](cli.md)
- You want the public analysis API or the result payload contract:
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

The compiler currently executes a complete front-end pipeline up through declaration resolution:

1. The CLI (`MahoCli` / `mahoc`) parses command-line arguments and configuration options.
2. If compiling a `.mhpr` project file or directory, `MahoBuild.MahoBuildSystem` discovers source files, parses configuration, and prepares the compilation.
3. `MahoCompiler.AnalyzeFiles(...)` coordinates multi-file front-end analysis (or `AnalyzeFile(...)` / `AnalyzeText(...)` for single inputs).
4. `SourceText` indexes line offsets and enables absolute and line/column span tracking.
5. `Lexer` and `Parser` generate a strongly-typed `SyntaxTree` with leading and trailing trivia.
6. `Resolver` executes the semantic resolution pipeline:
   - `SymbolDiscoveryPass`: discovers namespaces, builds the namespace trie, registers types/functions/variables/properties/aliases, assigns modifier flags (including `TypeFlags.Partial` and `FunctionFlags.Partial`), and constructs lexical scopes.
   - `DeclarationResolutionPass`: binds type references, verifies base type hierarchies, checks cyclic inheritance (`MH1004`), checks duplicate non-partial types (`MH1002`), performs partial type canonical merging and kind consistency, enforces partial function body limits and signature matching (`MH1003`), checks duplicate variables (`MH1005`), duplicate properties (`MH1006`), and ambiguous type references (`MH1001`).
7. Diagnostics are enriched with primary carets, secondary context spans, notes, and remediation help, then projected into `DiagnosticInfo` payloads.
8. The CLI renders either rich ANSI-colored reports or JSON envelopes for diagnostics and optional debug JSON.
9. Upon successful front-end resolution, the pipeline reaches the lowering/codegen boundary (currently throwing `MH9000`).

## Reading strategy

- If you are debugging behavior the user can see, start in the CLI docs and follow the call chain inward.
- If you are debugging spans, underlines, or line/column bugs, read `Text` before `Diagnostics` or renderer code.
- If you are changing AST shapes, read the folder guides under `Syntax` before touching parser code; the folders are organized by syntax category, not by parse phase.
- If you are adding new diagnostics, update both the internal diagnostics manager and the renderer logic that supplies tips and output formatting.
