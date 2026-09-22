# Maho

An experimental programming language and compiler project inspired by C#.

## Current Status

The repository is organized into four projects:

- `src/Maho/Maho.csproj`: the reusable core compiler library (source text, lexing, parsing, AST, diagnostics, and semantic resolution passes).
- `src/MahoBuild/MahoBuild.csproj`: the build system library (domain-specific `.mhpr` project file parsing, source file discovery, multi-project reference graph resolution).
- `src/MahoCli/MahoCli.csproj`: the command-line compiler driver (`mahoc`) and terminal diagnostic renderer.
- `tests/Maho.Tests/Maho.Tests.csproj`: unit and integration test suite (213+ automated tests).
- `maho.sln`: Visual Studio / OmniSharp solution file connecting all projects.

### Compiler Pipeline Capabilities

Today the compiler executes a complete front-end pipeline up through declaration resolution:

1. **Source Loading & Text Modeling**:
   - High-performance UTF-8/string line indexing and span arithmetic via `SourceText`.
   - File normalization and virtual in-memory document tracking via `SourceFile`.
2. **Lexing**:
   - Complete tokenization including combined operators (e.g. `??=`, `=>`, `::`), interpolated strings, raw string literals, numeric literals, and comments.
   - Preserves leading/trailing syntax trivia for IDE inspection and tooling.
3. **Parsing**:
   - Recursive descent parser with operator-precedence Pratt climbing.
   - Comprehensive AST nodes: declarations (classes, structs, interfaces, enums, functions, properties, fields, aliases), statements (control flow, assignments, local blocks, labels, gotos), and expressions.
   - Full opt-in top-level statement support via `#pragma toplevel enable` or project-level configuration.
4. **Build System & Project Graph (`MahoBuild`)**:
   - Parses domain-specific `.mhpr` project files with recursive source discovery patterns (`Directory`, `SourceFiles`, `ByName`).
   - Project-reference graph resolution allowing dependencies to be imported without polluting declaration scopes.
   - Completely decoupled from the core compiler library.
5. **Semantic Resolution Pipeline (`Resolver`)**:
   - **Symbol Discovery Pass (`SymbolDiscoveryPass`)**:
     - Discovers namespaces, qualified namespace paths, and constructs a hierarchical `NamespaceTrieNode`.
     - Identifies all types (classes, structs, interfaces, enums), functions, methods, properties, fields, global variables, and type aliases.
     - Assigns symbol flags (`SymbolFlags`), type flags (`TypeFlags.Partial`), and function flags (`FunctionFlags.Partial`).
     - Constructs lexical and member scopes with parent links.
   - **Declaration Resolution Pass (`DeclarationResolutionPass`)**:
     - **Non-Partial Duplicate Types**: Detects and reports duplicate type declarations with matching arities when at least one declaration is non-partial.
     - **Partial Types**: Merges matching partial declarations into canonical symbols; verifies `TypeKind` consistency across partial definitions.
     - **Partial Functions**: Allows unlimited bodyless partial declarations (supporting future macro implementations without spurious warnings); allows at most one declaration with an implementation body; enforces return type consistency.
     - **Duplicate Functions**: Detects duplicate non-partial function declarations matching in parameter types and generic arity.
     - **Duplicate Variables & Fields**: Flags duplicate global variables and struct/class fields.
     - **Duplicate Properties**: Flags duplicate property declarations within product types.
     - **Type Hierarchy & Cycles**: Resolves base types and detects cyclic inheritance chains via graph depth-first traversal.
     - **Ambiguous Type References**: Identifies collisions between imported/unqualified types from multiple namespaces.
     - **Type Constraints & Aliases**: Resolves generic parameter constraints, unwraps global and local aliases, and validates constraint compatibility.
6. **Diagnostics Engine**:
   - Rich two-span diagnostics with primary carets (`^^^^`), secondary context labels (`----`), informational notes, remediation advice, and suggestions.
   - ANSI color rendering in terminal output; structured JSON diagnostics format for IDEs and tooling.
7. **Lowering & Codegen**:
   - Intentionally terminates after declaration resolution with `MH9000` until semantic statement/expression type-checking and lowering passes are implemented.

## Documentation

Detailed subsystem guides are collected in [`docs/repository-guide.md`](docs/repository-guide.md):

- [`docs/cli.md`](docs/cli.md): command-line options and driver workflows.
- [`docs/compiler-library.md`](docs/compiler-library.md): architecture of the core `Maho` library and resolution passes.
- [`docs/analysis.md`](docs/analysis.md): public compilation, session, and diagnostic APIs.
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

## Building & CLI

Build the CLI compiler (`mahoc`) using the cross-platform build scripts:

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
- `./build-maho --single-file` (or `-s`): packages into a single binary executable (`mahoc` or `mahoc.exe`) with no loose DLLs, config JSON, or PDBs.
- `./build-maho --debug` (or `-d`): builds Debug configuration including PDB symbols and dependency JSON files.
- `./build-maho --all`: compiles for all major platforms (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`) into `dist/<platform>/`. Can be combined with `--single-file` or `--debug`.

Run the compiled CLI with:

```bash
./dist/mahoc [options] [source-path]
```

### Examples

```bash
# Compile a project file (.mhpr) with pretty diagnostics
./dist/mahoc Samples/Test.mhpr

# Inspect lexer tokens as JSON
./dist/mahoc --debug lex --output output/tokens.json Samples/Program.mh

# Inspect parser AST as JSON and output to stdout
./dist/mahoc --debug parse --output - Samples/Program.mh

# Output diagnostics in JSON format to stderr or file
./dist/mahoc --diagnostics json --output diagnostics.json Samples/Test.mhpr

# Enforce warnings as errors and full file paths
./dist/mahoc -Werror --diagnostic-paths full Samples/Test.mhpr

# Check compiler version or help
./dist/mahoc --version
./dist/mahoc --help
```

### Supported Flags

- `--debug (lex|parse)+ --output <path|->`: emit selected debug AST/token payloads to a file or `stdout`.
- `--diagnostics [pretty|text|json] --output <path|->`: emit diagnostics in rich pretty-printed format (default), short text format, or JSON to a file or `stderr`.
- `--color [auto|always|never]`: control ANSI colored terminal output.
- `--diagnostic-paths (relative|project|full)`: choose between relative paths (CWD, standard compiler default), project-relative paths, or full absolute paths in diagnostics.
- `--implicit-toplevel[=true|false]`: allow or disallow implicit top-level statements for entry files (defaults to true for single files, false for projects unless configured).
- `--no-project`: allow compiling directory sources directly when no `.mhpr` project file is present.
- `-Werror`, `--warnings-as-errors`: treat compiler warnings as errors.
- `-v`, `--version`: print the compiler version and exit.
- `-h`, `--help`: print usage information and exit.

When a directory path (or `.`) is provided, the CLI looks for a unique `.mhpr` project file in that directory. If no project file is found, `--no-project` must be supplied to analyze directory sources without a project file.

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

### Build System Library (`MahoBuild`)

- `MahoBuildSystem`: facade for project loading, compilation creation, source discovery, and build orchestration.
  - `MahoBuildSystem.LoadProject(projectFilePath, options)`
  - `MahoBuildSystem.CreateCompilation(projectFilePath, options)`
  - `MahoBuildSystem.AnalyzeProject(projectFilePath, output, options)`
  - `MahoBuildSystem.CompileProject(projectFilePath, output, options)`
  - `MahoBuildSystem.ResolveSourceFiles(path, config)`
  - `MahoBuildSystem.FindProjectFile(directoryPath)`
- `MahoProjectFileParser`: parses domain-specific `.mhpr` project configuration.

### CLI Library (`MahoCli`)

- `TerminalDiagnosticRenderer`: client-side terminal diagnostic renderer in `MahoCli.Diagnostics` producing ANSI reports with carets, line gutters, secondary labels, and suggestions.
- `CommandLine`: command-line parser and orchestration driver.