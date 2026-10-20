# Maho

The compiler and toolchain for the Maho programming language.

## Current Status

[![.NET](https://github.com/Luserus/maho/actions/workflows/dotnet.yml/badge.svg?branch=develop)](https://github.com/Luserus/maho/actions/workflows/dotnet.yml)

The repository is organized into focused projects:

- `src/Maho/Maho.csproj`: the reusable core compiler library (source text, lexing, parsing, AST, diagnostics, phase timers, macro expansion, and semantic resolution passes).
- `src/MahoServer/MahoServer.csproj`: the client/server protocol, package discovery, daemon client transport, and persistent session management.
- `src/MahoServerHost/MahoServerHost.csproj`: the generic persistent compiler daemon (`mahoc-server`) communicating over named pipes / domain sockets for low-latency builds.
- `src/MahoCompilerHost/MahoCompilerHost.csproj`: the package-local compiler host executable (`maho-comp-host`) dynamically loading and executing compiler packages.
- `src/Miryo/Miryo.csproj`: the project manager and build tool (`miryo`), including `Miryo.Build` for project configuration, source discovery, pipeline orchestration, and server lifecycle management.
- `src/MahoCli/MahoCli.csproj`: the command-line compiler driver (`mahoc`) and terminal diagnostic renderer.
- `tests/Maho.Tests/Maho.Tests.csproj`: comprehensive automated test suite (450 automated tests).
- `maho.sln`: Visual Studio / OmniSharp solution file connecting all projects.

### Compiler Pipeline Capabilities

Today the compiler executes a complete front-end pipeline up through declaration resolution and macro expansion:

1. **Source Loading & Text Modeling**:
   - High-performance UTF-8/string line indexing and span arithmetic via `SourceText`.
   - File normalization and virtual in-memory document tracking via `SourceFile`.
2. **Lexing**:
   - Complete tokenization including combined operators (e.g. `??=`, `=>`, `::`), interpolated strings, raw string literals, numeric literals, and comments.
   - Preserves leading/trailing syntax trivia for IDE inspection and tooling.
3. **Parsing**:
   - Recursive descent parser with operator-precedence Pratt climbing.
   - Comprehensive AST nodes: declarations (classes, structs, interfaces, enums, functions, properties, fields, aliases), statements, and expressions.
   - Unified control-flow expressions: `if` and `while` loops as expressions (`WhileExpression`), with optional variable declarations in conditional headers.
   - Full opt-in top-level statement support via `#pragma toplevel enable` or CLI/project-level configuration.
4. **Build System & Project Graph (`Miryo.Build` & `Miryo`)**:
   - Independent build orchestrator parsing `.mhpr` project files and configuring builds via `miryo.config`.
   - Discovers sources, builds project dependency graphs, and orchestrates compiler invocations.
   - Rich staged pipeline reporting (`Analyzing project configuration...`, `Executing core compile...`).
   - Completely decoupled from specific compiler implementations via versioned compiler packages.
5. **Persistent Compiler Server Daemon (`MahoServer` & `MahoServerHost`)**:
   - Spawns background compiler daemons per user and compiler identity communicating via IPC pipes/sockets.
   - Preserves warmed-up JIT and assembly state, reducing subsequent compilation times from ~100ms down to sub-millisecond latencies.
   - Supports self-contained versioned packages (`compilers/<version>/compiler.json`) hosted through `maho-comp-host`.
6. **Macro Expansion Pass (`MacroExpansionPass`)**:
   - Multi-pass macro expansion with configurable recursion limits (`MacroRecursionLimit`).
   - Nested macro invocation tracking and box-tree diagnostic tracebacks (`└── from this macro invocation`).
7. **Semantic Resolution Pipeline (`Resolver`)**:
   - **Symbol Discovery Pass (`SymbolDiscoveryPass`)**:
     - Discovers namespaces, qualified namespace paths, and constructs a hierarchical `NamespaceTrieNode`.
     - Identifies all types (classes, structs, interfaces, enums), functions, methods, properties, fields, global variables, and type aliases.
     - Assigns symbol flags (`SymbolFlags`), type flags (`TypeFlags.Partial`), and function flags (`FunctionFlags.Partial`).
     - Constructs lexical and member scopes with parent links.
   - **Declaration Resolution Pass (`DeclarationResolutionPass`)**:
     - **Non-Partial Duplicate Types**: Detects and reports duplicate type declarations with matching arities when at least one declaration is non-partial.
     - **Partial Types**: Merges matching partial declarations into canonical symbols; verifies `TypeKind` consistency across partial definitions.
     - **Partial Functions**: Allows unlimited bodyless partial declarations; allows at most one declaration with an implementation body; enforces return type consistency.
     - **Duplicate Functions**: Detects duplicate non-partial function declarations matching in parameter types and generic arity.
     - **Duplicate Variables & Fields**: Flags duplicate global variables and struct/class fields.
     - **Duplicate Properties**: Flags duplicate property declarations within product types.
     - **Type Hierarchy & Cycles**: Resolves base types and detects cyclic inheritance chains via graph depth-first traversal.
     - **Ambiguous Type References**: Identifies collisions between imported/unqualified types from multiple namespaces.
     - **Type Constraints & Aliases**: Resolves generic parameter constraints, unwraps global and local aliases, and validates constraint compatibility.
8. **Diagnostics Engine & Phase Timing**:
   - Rich two-span diagnostics with primary carets (`^^^^`), secondary context labels (`----`), informational notes, remediation advice, and suggestions.
   - Core compiler tracks pipeline phase durations (`Syntax`, `SemanticAnalysis`, `Lowering`, and `Total`), returning them in analysis results.
   - ANSI color rendering in terminal output with formatted status lines (`Build succeeded in <time>`, `Build failed with <n> error(s)`).
9. **Lowering & Codegen**:
   - Intentionally terminates after declaration resolution with `MH9000` until semantic statement/expression lowering passes are implemented.

## Documentation

Detailed subsystem guides are collected in [`docs/repository-guide.md`](docs/repository-guide.md):

- [`docs/miryo.md`](docs/miryo.md): build tool, `.mhpr` project files, and `miryo.config`.
- [`docs/cli.md`](docs/cli.md): `mahoc` compiler CLI options and driver workflows.
- [`docs/compiler-library.md`](docs/compiler-library.md): architecture of the core `Maho` library and resolution passes.
- [`docs/analysis.md`](docs/analysis.md): public compilation, session, phase timers, and diagnostic APIs.
- [`docs/diagnostics.md`](docs/diagnostics.md): internal diagnostics model and reporters.
- [`docs/language-theory.md`](docs/language-theory.md): grammar specifications, AST taxonomy, and resolution semantics.
- [`docs/source-tree.md`](docs/source-tree.md): repository directory and project layout.

## Building & Testing

This project requires the .NET 10 SDK (`net10.0`).

Run the automated test suite:

```bash
dotnet test
```

Build the entire solution with `dotnet`:

```bash
dotnet build maho.sln
```

## Toolchain & CLI

Build the toolchain binaries (`mahoc` and `miryo`) using the cross-platform build scripts:

```bash
# Linux / macOS / WSL / Git Bash
./build-maho

# Windows PowerShell
.\build-maho.ps1

# Windows Command Prompt
build-maho
```

### Build Options

- `./build-maho`: clean release build for host platform into `dist/` (omits `.pdb` and `.deps.json` files).
- `./build-maho --single-file` (or `-s`): packages into single binary executables (`mahoc`, `miryo`) with no loose DLLs or config files.
- `./build-maho --debug` (or `-d`): builds Debug configuration including PDB symbols and dependency JSON files.
- `./build-maho --all`: compiles for all major platforms (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`) into `dist/<platform>/`.

### Running the Project Manager (`miryo`)

```bash
# Create a new binary or library project
./dist/miryo new MyApp
./dist/miryo new --lib MyLib

# Build or type-check a project
./dist/miryo build
./dist/miryo build --check

# Run a project (passes runtime args after --)
./dist/miryo run -- arg1 arg2

# Clean target and obj output directories
./dist/miryo clean

# Gracefully stop background compiler daemon servers
./dist/miryo compiler-server shutdown
```

### Running the Direct Compiler (`mahoc`)

```bash
# Compile or type-check source files directly
./dist/mahoc --check src/Program.mh
./dist/mahoc --check src/

# Specify an explicit compiler package or server executable
./dist/mahoc --compiler-package dist/compilers/0.2.0 --check src/Program.mh

# Pass global type aliases
./dist/mahoc --check --alias int32=Std.Int32 src/

# Inspect AST or tokens as JSON
./dist/mahoc --debug parse -o - src/Program.mh
```

## Project Files (`.mhpr`)

Maho projects can be configured with `.mhpr` project files:

```mhpr
EntryFile : "Program.mh";
ImplicitTopLevel : true;
GlobalUnsafeEnabled : false;
ProjectsReferenced : [
    "../Core/Core.mhpr"
];
Sources : {
    Directory : "$",
    SourceFiles : [ "Program.mh" ],
    ByName : "*.mh"
};
GlobalAliases : {
    "int32" : "Std.Int32",
    "string" : "Std.String8"
};
```

- `EntryFile`: specifies the designated entry-point file.
- `ImplicitTopLevel`: when `true`, allows top-level statements in the entry file without requiring `#pragma toplevel enable`. If no `EntryFile` is configured, the single file containing top-level statements is automatically selected as the entry point.
- `ProjectsReferenced`: referenced `.mhpr` projects whose exported symbols are imported into the compilation scope.
- `Sources`: specifies source file discovery options via a dictionary (`Directory`, `SourceFiles`, `ByName`) or a shorthand array of file paths.
- `GlobalAliases`: project-wide type aliases.

## Library API

### Core Compiler Library (`Maho`)

- `Compilation`: batch compilation coordinating source files, syntax trees, project references, and semantic resolution.
  - `Compilation.FromSource(code, path, options)`
  - `Compilation.FromFiles(filePaths, projectName, options, referencedCompilations)`
  - `compilation.CreateSession()`
- `AnalysisSession`: stateful incremental compilation for interactive REPLs and CLI interpreters.
  - `session.AnalyzeSnippet(code)`: parses and resolves isolated expressions, statements, or declarations against previous session symbols.
  - `session.CommitSnippet(result)`: incorporates successful declarations into the active session scope.
- `MahoCompiler`: high-level facade for analyzing single files, in-memory strings, or file batches.

### Build System & Toolchain (`Miryo` & `Miryo.Build`)
 
- `MiryoBuildSystem`: project loading, source discovery, and pipeline orchestration.
  - `MiryoBuildSystem.LoadProject(projectFilePath)`
  - `MiryoBuildSystem.ResolveSourceFiles(path, config)`
  - `MiryoBuildSystem.FindProjectFile(directoryPath)`
- `MahoProjectFileParser`: parses domain-specific `.mhpr` project configuration.
- `MiryoConfiguration`: parses `miryo.config` INI configuration and resolves tool paths (`mahoc`, etc.).

### CLI Compiler (`MahoCli` / `mahoc`)

- `TerminalDiagnosticRenderer`: client-side terminal diagnostic renderer in `MahoCli.Diagnostics` producing ANSI reports with carets, line gutters, secondary labels, and suggestions.
- `CommandLine`: command-line parser and compiler frontend driver.
