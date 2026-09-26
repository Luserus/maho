# CLI Architecture and Driver Guide

`MahoCli` (compiled as the executable `mahoc`) is the command-line driver and diagnostic rendering frontend for the Maho language compiler. It orchestrates input parsing, delegates compilation to the core `Maho` library, and formats diagnostics, pipeline phase timers, and debug AST output.

*(Note: For project scaffolding, `.mhpr` project file management, and multi-project dependency resolution, see [`miryo.md`](miryo.md). `mahoc` operates directly on Maho source files and directories without project-file coupling.)*

---

## 1. Building the Toolchain

The repository includes cross-platform build scripts that compile and publish `mahoc`, `miryo`, and `Maho.dll` into the root `dist/` directory:

```bash
# Linux / macOS / WSL / Git Bash
./build-maho

# Windows PowerShell
.\build-maho.ps1

# Windows Command Prompt
build-maho
```

### Build Options

- (default): Release build for the host platform into `dist/`, omitting `.pdb` and `.deps.json` files for clean output.
- `--single-file` (or `-s`): Bundles managed assemblies and runtime configuration into single standalone binary executables (`mahoc`, `miryo`) with zero loose runtime files.
- `--debug` (or `-d`): Builds Debug configuration and emits `.pdb` symbols and `.deps.json`.
- `--all`: Compiles and publishes for all major platforms (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`) into `dist/<platform>/`.

---

## 2. Running the Compiler

Run the compiled executable directly on source files or directories:

```bash
./dist/mahoc [options] [source-paths...]
```

`source-paths` can be:
- A single `.mh` source file.
- Multiple `.mh` source files (e.g. `./dist/mahoc Types.mh Program.mh`).
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
| `--debug` | `(lex\|parse)+` | Selects which debug AST/token JSON views to generate. |
| `--diagnostics` | `pretty`, `text`, `json` | Diagnostic output formatting. Defaults to `pretty`. |
| `-np`, `--no-pretty` | Flag | Shortcut to disable pretty terminal styling and colors. |
| `-o`, `--output` | `<file\|->` | Output target path (`-` selects `stdout` for debug JSON, `stderr` for diagnostics). |
| `--color` | `auto`, `always`, `never` | Controls ANSI color formatting in terminal output. |
| `--diagnostic-paths` | `relative`, `project`, `full` | Selects whether diagnostic file paths are relative to CWD, relative to project root, or full absolute paths. |
| `--implicit-toplevel` | `true`, `false` | Controls whether top-level statements are implicitly allowed for entry files without `#pragma toplevel enable`. |
| `-Werror`, `--warnings-as-errors` | Flag | Treats compiler warnings as errors. |
| `-v`, `--version` | Flag | Prints compiler version and exits. |
| `-h`, `--help` | Flag | Prints usage instructions and exits. |

---

## 4. Debug and Diagnostics Output Routing

Syntax trees and token streams can be inspected via `--debug`:

```bash
# Output lexer tokens as JSON to a file
./dist/mahoc --debug lex -o tokens.json src/Program.mh

# Output parser AST as JSON to stdout
./dist/mahoc --debug parse -o - src/Program.mh

# Output both lexer and parser debug views
./dist/mahoc --debug lex parse -o debug.json src/
```

Diagnostics and status lines are written to `stderr` (or an explicit destination provided via `-o`):

```bash
# Pretty-printed ANSI diagnostics with gutters, carets, and notes
./dist/mahoc src/

# Output machine-readable JSON diagnostics to stderr
./dist/mahoc --diagnostics json -o - src/

# Output plain-text diagnostics to a file
./dist/mahoc --diagnostics text -o report.txt src/
```

---

## 5. Terminal Diagnostic Rendering & Status Lines

`TerminalDiagnosticRenderer` provides Rust-style diagnostic rendering:
- Source line gutters with 1-based line numbers.
- Primary carets (`^^^^`) indicating the exact fault location.
- Secondary labels (`----`) highlighting related context (e.g. earlier declaration sites, base types, conflicting bodies).
- Informational notes, actionable remediation hints (`help:`), and automated fix suggestions.

### Status Line Formatting

At the conclusion of compilation, the compiler outputs a standardized status line with elapsed time measured directly by the core compiler:

- **Success with zero warnings**:
  ```
  Build succeeded in 42.15ms
  ```
  *(word `succeeded` formatted in bold green)*

- **Success with warnings**:
  ```
  Build succeeded with 2 warning(s) in 55.10ms
  ```
  *(word `succeeded` in bold green, `warning(s)` in bold yellow)*

- **Build failure with errors only**:
  ```
  Build failed with 1 error(s)
  ```
  *(word `error(s)` formatted in bold red)*

- **Build failure with errors and warnings**:
  ```
  Build failed with 2 error(s) and 1 warning(s)
  ```
  *(word `error(s)` in bold red, `warning(s)` in bold yellow)*

Elapsed time is dynamically rendered with human-readable units: `s` for $\ge 1.0\,\text{s}$, `ms` for $\ge 1.0\,\text{ms}$, and `ns` for sub-millisecond durations.

---

## 6. Execution Lifecycle

1. `CommandLine.Run(args)` parses CLI arguments and flags.
2. `ResolveFiles` locates and sorts all matching `.mh` source files based on inputs, glob patterns, and recursion settings.
3. The compiler runs the complete front-end pipeline:
   - **Syntax Phase**: Parallel lexing and parsing across all source files, constructing strongly-typed ASTs and tracking syntax elapsed time (`CompilationPhaseTimers.Syntax`).
   - **Semantic Phase**: Multi-pass resolution (`SymbolDiscoveryPass` and `DeclarationResolutionPass`), tracking semantic elapsed time (`CompilationPhaseTimers.SemanticAnalysis`).
4. If errors are present, `TerminalDiagnosticRenderer` formats and displays diagnostics on `stderr`.
5. Upon successful front-end resolution, the driver enters the lowering phase (currently throwing `MH9000: The lowering and code-generation pipeline has not been implemented.` until backend codegen is completed).
6. The driver renders the status line using `analysis.Elapsed` and exits with code `0` on success or `1` on error.
