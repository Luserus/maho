# CLI Architecture and Driver Guide

`MahoCli` (compiled as the executable `mahoc`) is the command-line driver and diagnostic rendering frontend for the Maho language compiler. It orchestrates input parsing, delegates compilation to the core `Maho` library, and formats diagnostics, pipeline phase timers, and debug AST output.

*(Note: For project scaffolding, `.mhpr` project file management, and multi-project dependency resolution, see [`miryo.md`](miryo.md). `mahoc` operates directly on Maho source files and directories without project-file coupling.)*

---

## 1. Building the Toolchain

The repository includes cross-platform build scripts that compile and publish `mahoc`, `miryo`, and `Maho.dll` into the root `bin/` directory:

```bash
# Linux / macOS / WSL / Git Bash
./build-maho

# Windows PowerShell
.\build-maho.ps1

# Windows Command Prompt
build-maho
```

### Build Options

- (default): Release build for the host platform into `bin/` (`bin/maho/` and `bin/miryo/`), omitting `.pdb` and `.deps.json` files for clean output.
- `--single-file` (or `-s`): Bundles managed assemblies and runtime configuration into single standalone binary executables with shared runtimes.
- `--debug` (or `-d`): Builds Debug configuration and emits `.pdb` symbols and `.deps.json`.
- `--all`: Compiles and publishes for all major platforms (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`) into `bin/<platform>/`.

---

## 2. Running the Compiler

Run the compiled executable directly on source files or directories:

```bash
./bin/maho/shims/mahoc [options] [source-paths...]
```

`source-paths` can be:
- A single `.mh` source file.
- Multiple `.mh` source files (e.g. `./bin/maho/shims/mahoc Types.mh Program.mh`).
- A directory containing `.mh` source files (searched recursively by default, or shallow with `--no-recurse`).
- When omitted, `mahoc` defaults to scanning the current working directory (`.`).

---

## 3. Command-Line Options

| Option | Values / Format | Description |
|---|---|---|
| `--check` | Flag | Analyzes, resolves, and type-checks sources without lowering or generating code. |
| `--emit-il` | Flag | Requests emission of intermediate language representation (`.mil`). |
| `--entry` | `<file>` | Designates a specific source file as the explicit entry point. |
| `--no-recurse` | Flag | When passing a directory, analyzes only the top-level directory without scanning subdirectories. |
| `--pattern` | `<glob>` | Source file matching glob pattern (defaults to `*.mh`). |
| `--alias`, `--global-alias` | `<alias>=<target>` | Declares a project-wide type alias (e.g. `--alias int32=Std.Int32`). |
| `--unsafe` | Flag | Enables unsafe operations globally. |
| `--compiler-package` | `<path>` | Compiler package directory or `compiler.json` manifest. |
| `--server-path` | `<path>` | Explicit path to the `mahoc-server` executable. |
| `--diagnostics` | `pretty`, `text`, `json` | Diagnostic output formatting. Defaults to `pretty`. |
| `-np`, `--no-pretty` | Flag | Shortcut to disable pretty terminal styling and colors. |
| `-o`, `--output` | `<file\|->` | Output target path (`-` selects `stderr` for diagnostics). |
| `--color` | `auto`, `always`, `never` | Controls ANSI color formatting in terminal output. |
| `--diagnostic-paths` | `relative`, `project`, `full` | Selects whether diagnostic file paths are relative to CWD, relative to project root, or full absolute paths. |
| `--implicit-toplevel` | `true`, `false` | Controls whether top-level statements are implicitly allowed for entry files without `#pragma toplevel enable`. |
| `-Werror`, `--warnings-as-errors` | Flag | Treats compiler warnings as errors. |
| `-v`, `--version` | Flag | Prints compiler version and exits. |
| `-h`, `--help` | Flag | Prints usage instructions and exits. |

---

## 4. Diagnostics Output Routing

Diagnostics are written to `stderr` by default (or an explicit destination provided via `-o`):

```bash
# Pretty-printed ANSI diagnostics with gutters, carets, and notes
./bin/maho/shims/mahoc src/

# Output machine-readable JSON diagnostics to stderr
./bin/maho/shims/mahoc --diagnostics json -o - src/

# Output plain-text diagnostics to a file
./bin/maho/shims/mahoc --diagnostics text -o report.txt src/
```

---

## 5. Terminal Diagnostic Rendering & Status Lines

`TerminalDiagnosticRenderer` provides Rust-style diagnostic rendering:
- Source line gutters with 1-based line numbers.
- Primary carets (`^^^^`) indicating the exact fault location.
- Secondary labels (`----`) highlighting related context (e.g. earlier declaration sites, base types, conflicting bodies).
- Informational notes, actionable remediation hints (`help:`), and automated fix suggestions.

### Status Line Formatting

- **Successful compilation**:
  `mahoc` outputs nothing (silent success) and exits with code `0`.

- **Failed compilation**:
  Diagnostics are displayed followed by a concise failure count:
  ```
  compilation failed due to 1 error
  ```
  or:
  ```
  compilation failed due to 2 errors
  ```
  *(Timing metrics and execution durations are owned and formatted by `miryo`, the toolchain manager and build orchestrator).*

---

## 6. Execution Lifecycle

1. `CommandLine.Run(args)` parses CLI arguments and flags.
2. `ResolveFiles` locates and sorts all matching `.mh` source files based on inputs, glob patterns, and recursion settings.
3. The compiler runs the complete front-end pipeline:
   - **Syntax Phase**: Parallel lexing and parsing across all source files, constructing strongly-typed ASTs and tracking syntax elapsed time (`CompilationPhaseTimers.Syntax`).
   - **Semantic Phase**: Multi-pass resolution (`SymbolDiscoveryPass` and `DeclarationResolutionPass`), tracking semantic elapsed time (`CompilationPhaseTimers.SemanticAnalysis`).
4. If errors are present, `TerminalDiagnosticRenderer` formats and displays diagnostics on `stderr`, followed by the failure status line, and exits with code `1`.
5. Upon successful front-end resolution, the driver enters the lowering phase (currently throwing `MH9000: The lowering and code-generation pipeline has not been implemented.` until backend codegen is completed) or exits silently with code `0`.
