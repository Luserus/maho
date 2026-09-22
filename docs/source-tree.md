# Source Tree Guide

The repository is organized into focused projects:

## Core Projects (`src/`)

- [`compiler-library.md`](compiler-library.md) (`src/Maho`): the reusable compiler library containing source text modeling, lexer, parser, AST, diagnostics engine, and multi-pass semantic resolver (`SymbolDiscoveryPass` and `DeclarationResolutionPass`).
- `MahoBuild` (`src/MahoBuild`): the isolated build system library handling `.mhpr` project file parsing, file pattern discovery, and project-graph dependency management.
- [`cli.md`](cli.md) (`src/MahoCli`): the executable compiler driver (`mahoc`) and terminal diagnostic renderer.

## Tests (`tests/`)

- `tests/Maho.Tests`: complete xUnit test suite covering lexer, parser, diagnostics engine, resolution passes, CLI argument parsing, and terminal rendering.

## Distribution & Build (`dist/` & Root Scripts)

- `dist/`: git-tracked output directory containing compiled compiler binaries (`mahoc`, `mahoc.exe`, and platform subdirectories).
- `build-maho`: cross-platform Bash build script for Linux, macOS, WSL, and Git Bash.
- `build-maho.ps1`: PowerShell build script for Windows and PowerShell Core.
- `build-maho.cmd`: Windows Command Prompt launcher.

## How to Traverse the Codebase

- Start in [`cli.md`](cli.md) if your question is about command-line behavior, build scripts, argument parsing, output files, or terminal rendering.
- Start in [`compiler-library.md`](compiler-library.md) if your question is about source text loading, diagnostics, AST structure, analysis results, or semantic resolution passes.
- Start in [`language-theory.md`](language-theory.md) if your question is about the formal grammar, operator precedence, AST hierarchy, or resolution invariants.

## Suggested Reading Order

1. [`cli.md`](cli.md)
2. [`compiler-library.md`](compiler-library.md)
3. [`language-theory.md`](language-theory.md)
4. Subsystem-specific guides ([`source-text.md`](source-text.md), [`syntax.md`](syntax.md), [`diagnostics.md`](diagnostics.md), [`analysis.md`](analysis.md))
