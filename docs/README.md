# Maho Repository Guide

This folder is the "where do I go next?" map for the repository.

The root [`README.md`](../README.md) is still the right place for build, run, and CLI basics. This guide helps you jump to the subsystem that owns the behavior you are looking at.

## Repository shape

- [`src/docs/README.md`](../src/docs/README.md): start here if you want the project-level split between the reusable compiler library and the CLI.
- [`src/Maho/docs/README.md`](../src/Maho/docs/README.md): map of the core library.
- [`src/Maho.Cli/docs/README.md`](../src/Maho.Cli/docs/README.md): full CLI control-flow guide, including argument parsing, file batching, status output, and rendering.

## Go here when...

- You want to see how `./maho` becomes actual work:
  [`src/Maho.Cli/docs/README.md`](../src/Maho.Cli/docs/README.md)
- You want the public analysis API or the result payload contract:
  [`src/Maho/Analysis/docs/README.md`](../src/Maho/Analysis/docs/README.md)
- You want to trace diagnostics from creation to final text/json output:
  [`src/Maho/Diagnostics/docs/README.md`](../src/Maho/Diagnostics/docs/README.md)
  then [`src/Maho.Cli/docs/README.md`](../src/Maho.Cli/docs/README.md)
- You want to understand debug JSON and how lexer/parser state is serialized:
  [`src/Maho/Analysis/docs/README.md`](../src/Maho/Analysis/docs/README.md)
  and [`src/Maho/Syntax/docs/README.md`](../src/Maho/Syntax/docs/README.md)
- You want source text loading, line lookup, or span math:
  [`src/Maho/Text/docs/README.md`](../src/Maho/Text/docs/README.md)
- You want the syntax tree layout, without diving into parser internals immediately:
  [`src/Maho/Syntax/docs/README.md`](../src/Maho/Syntax/docs/README.md)
- You want the AST categories:
  [`src/Maho/Syntax/Declarations/docs/README.md`](../src/Maho/Syntax/Declarations/docs/README.md),
  [`src/Maho/Syntax/Expressions/docs/README.md`](../src/Maho/Syntax/Expressions/docs/README.md),
  [`src/Maho/Syntax/Fragments/docs/README.md`](../src/Maho/Syntax/Fragments/docs/README.md),
  [`src/Maho/Syntax/Statements/docs/README.md`](../src/Maho/Syntax/Statements/docs/README.md)
- You want the semantic placeholders that exist today:
  [`src/Maho/Symbols/docs/README.md`](../src/Maho/Symbols/docs/README.md)
  and [`src/Maho/Resolution/docs/README.md`](../src/Maho/Resolution/docs/README.md)

## Current pipeline

At the moment, the codebase is strongest in the front half of the compiler:

1. The CLI resolves input files and options.
2. `MahoCompiler.AnalyzeFiles(...)` owns batch analysis, while `AnalyzeFile(...)` and `AnalyzeText(...)` handle single inputs.
3. `MahoCompiler` loads source text, lexes, parses, and starts project-wide resolution.
4. Diagnostics are projected into a public, serializable result model.
5. Optional debug JSON is produced for lexer and parser state.
6. The CLI renders either text output or JSON envelopes for both debug data and diagnostics.

Resolution and symbol work are present as a real semantic scaffold, but the layer is still evolving.

## Reading strategy

- If you are debugging behavior the user can see, start in the CLI docs and follow the call chain inward.
- If you are debugging spans, underlines, or line/column bugs, read `Text` before `Diagnostics` or renderer code.
- If you are changing AST shapes, read the folder guides under `Syntax` before touching parser code; the folders are organized by syntax category, not by parse phase.
- If you are adding new diagnostics, update both the internal diagnostics manager and the renderer logic that supplies tips and output formatting.
