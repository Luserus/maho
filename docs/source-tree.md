# Source Tree Guide

The repository is organized into focused projects:

## Core Projects (`src/`)

- [`compiler-library.md`](compiler-library.md) (`src/Maho`): the reusable compiler library containing source text modeling, lexer, parser, AST, diagnostics engine, phase timers, and multi-pass semantic resolver (`SymbolDiscoveryPass` and `DeclarationResolutionPass`).
- [`miryo.md`](miryo.md) (`src/Miryo`): the project manager and build tool CLI (`miryo`), containing `Miryo.Build` for `.mhpr` project file parsing, source discovery, project-graph dependency management, and toolchain process execution.
- [`cli.md`](cli.md) (`src/MahoCli`): the executable compiler driver (`mahoc`) and terminal diagnostic renderer.

## Tests (`tests/`)

- `tests/Maho.Tests`: complete xUnit test suite covering lexer, parser, diagnostics engine, resolution passes, CLI argument parsing, Miryo project loading, and terminal rendering.

## Distribution & Build (`dist/` & Root Scripts)

- `dist/`: output directory containing compiled binaries (`mahoc`, `miryo`, `Maho.dll`, and platform subdirectories).
- `miryo.config`: default toolchain configuration defining tool search paths and project templates.
- `build-maho`: cross-platform Bash build script for Linux, macOS, WSL, and Git Bash.
- `build-maho.ps1`: PowerShell build script for Windows and PowerShell Core.
- `build-maho.cmd`: Windows Command Prompt launcher.

## How to Traverse the Codebase

- Start in [`miryo.md`](miryo.md) if your question is about project scaffolding, `.mhpr` files, dependencies, or high-level build commands (`miryo new`, `miryo build`, `miryo run`).
- Start in [`cli.md`](cli.md) if your question is about direct compiler CLI behavior (`mahoc`), file inputs, flags, diagnostics formatting, or terminal rendering.
- Start in [`compiler-library.md`](compiler-library.md) if your question is about source text loading, diagnostics, AST structure, phase timers, analysis results, or semantic resolution passes.
- Start in [`language-theory.md`](language-theory.md) if your question is about the formal grammar, operator precedence, AST hierarchy, or resolution invariants.

## Suggested Reading Order

1. [`miryo.md`](miryo.md) & [`cli.md`](cli.md)
2. [`compiler-library.md`](compiler-library.md)
3. [`language-theory.md`](language-theory.md)
4. Subsystem-specific guides ([`source-text.md`](source-text.md), [`syntax.md`](syntax.md), [`diagnostics.md`](diagnostics.md), [`analysis.md`](analysis.md))
