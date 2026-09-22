# CLI Architecture and Driver Guide

`MahoCli` (compiled as the executable `mahoc`) is the command-line driver and diagnostic rendering frontend for the Maho language compiler. It orchestrates input parsing, delegates compilation to the core `Maho` library and `MahoBuild` build system, and formats diagnostics and debug AST output.

---

## 1. Building the CLI

The repository includes cross-platform build scripts that publish the driver executable directly into the root `dist/` directory:

```bash
# Linux / macOS / WSL / Git Bash
./build-maho

# Windows PowerShell
.\build-maho.ps1

# Windows Command Prompt
build-maho
```

### Build Options

- (default): Release build for the host platform into `dist/mahoc`, omitting `.pdb` and `.deps.json` files for clean output.
- `--single-file` (or `-s`): Bundles managed assemblies and runtime configuration into a single standalone binary executable (`mahoc` or `mahoc.exe`) with zero loose non-binary files.
- `--debug` (or `-d`): Builds Debug configuration and emits `.pdb` symbols and `.deps.json`.
- `--all`: Compiles and publishes for all major platforms (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`) into `dist/<platform>/`.

---

## 2. Running the Compiler

Run the compiled executable directly:

```bash
./dist/mahoc [options] [source-path]
```

`source-path` can be:
- A single `.mh` source file.
- A domain-specific `.mhpr` project file.
- A directory containing source files (automatically searches for a `.mhpr` project file; if absent, `--no-project` must be supplied).

---

## 3. Command-Line Options

| Option | Values / Format | Description |
|---|---|---|
| `--debug` | `(lex\|parse)+` | Selects which debug AST/token JSON views to generate. Requires `-o` or `--output`. |
| `--diagnostics` | `pretty`, `text`, `json` | Diagnostic output formatting. Defaults to `pretty`. |
| `-o`, `--output` | `<file\|->` | Output target path (`-` selects `stdout` for debug JSON, `stderr` for diagnostics). |
| `--color` | `auto`, `always`, `never` | Controls ANSI color formatting in terminal output. |
| `--diagnostic-paths` | `relative`, `project`, `full` | Selects whether diagnostic file paths are relative to CWD, relative to project root, or full absolute paths. |
| `--implicit-toplevel` | `true`, `false` | Controls whether top-level statements are implicitly allowed for entry files without `#pragma toplevel enable`. |
| `--no-project` | Flag | Permits compiling directory sources directly without a `.mhpr` project file. |
| `-Werror`, `--warnings-as-errors` | Flag | Treats compiler warnings as errors. |
| `-v`, `--version` | Flag | Prints compiler version and exits. |
| `-h`, `--help` | Flag | Prints usage instructions and exits. |

---

## 4. Debug and Diagnostics Output Routing

Syntax trees and token streams are inspection data:

```bash
# Output lexer tokens as JSON to a file
./dist/mahoc --debug lex -o tokens.json Samples/Program.mh

# Output parser AST as JSON to stdout
./dist/mahoc --debug parse -o - Samples/Program.mh

# Output both lexer and parser debug views
./dist/mahoc --debug lex parse -o debug.json Samples/Test.mhpr
```

Diagnostics are output to `stderr` by default using rich terminal formatting:

```bash
# Pretty-printed ANSI diagnostics with gutters and labels
./dist/mahoc Samples/Test.mhpr

# Output machine-readable JSON diagnostics to stderr
./dist/mahoc --diagnostics json -o - Samples/Test.mhpr

# Output diagnostics to a file
./dist/mahoc --diagnostics text -o report.txt Samples/Test.mhpr
```

Using `-` for `--output` directs debug data to `stdout` and diagnostic reports to `stderr`, allowing simultaneous redirection and piping.

---

## 5. Terminal Diagnostic Rendering

`MahoCli.Diagnostics.TerminalDiagnosticRenderer` provides Rust-style diagnostic rendering:
- Source line gutters with 1-based line numbers.
- Primary carets (`^^^^`) indicating the exact fault location.
- Secondary labels (`----`) highlighting related context (e.g. earlier declaration sites, base types, conflicting bodies).
- Informational notes, actionable remediation hints (`help:`), and automated fix suggestions.

---

## 6. Execution Lifecycle

1. `CommandLine.Run(args)` parses arguments, flags, and paths.
2. If the input is a project file or directory, `MahoBuildSystem` discovers source files, parses `.mhpr` settings, and prepares a `Compilation`.
3. The compiler runs the complete front-end pipeline:
   - Lexer tokenizes source into `SyntaxToken` with trivia.
   - Parser generates a strongly-typed `SyntaxTree`.
   - `Resolver` runs semantic passes (`SymbolDiscoveryPass` and `DeclarationResolutionPass`).
4. If errors are present, `TerminalDiagnosticRenderer` formats and displays diagnostics on `stderr`.
5. Upon successful front-end resolution, the driver enters the lowering phase (currently throwing `MH9000: The lowering and code-generation pipeline has not been implemented.`).
