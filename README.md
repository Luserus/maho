# Maho

An experimental programming language and compiler project inspired by C#.

## Current Status

The repository is split into three main components:

- `src/Maho/Maho.csproj`: the reusable core compiler library.
- `src/Maho.Cli/Maho.Cli.csproj`: the command-line driver and terminal renderer.
- `tests/Maho.Tests/Maho.Tests.csproj`: unit and integration test suite.
- `Maho.sln`: solution file for editor/LSP support across all projects.

Today the compiler can:

- Load and process `.mh` source files, directories, and domain-specific `.mhpr` project files.
- Lex and parse source files into strongly-typed syntax trees.
- Emit structured lexer and parser debug views as JSON.
- Run project-wide semantic resolution across compilation units, including symbol discovery, declaration resolution, alias unwrapping, base type inheritance, and generic constraints.
- Support multi-project references (`.mhpr`) with imported symbol tables without cross-project declaration pollution.
- Provide stateful interactive evaluation via `AnalysisSession` for REPLs and CLI interpreters, allowing isolated code snippets to be analyzed incrementally against prior symbol tables.
- Render rich, terminal diagnostics with ANSI colors, line gutters, primary carets (`^^^^`), secondary context dashes (`----`), informational notes, remediation help, and code fix suggestions.

The semantic pipeline is actively expanding, with runtime intrinsic types discovery underway. Code generation and lowering will follow once semantic passes are complete.

## Documentation

Detailed subsystem guides are collected in [`docs/repository-guide.md`](docs/repository-guide.md):

- [`docs/cli.md`](docs/cli.md): command-line options and driver workflows.
- [`docs/compiler-library.md`](docs/compiler-library.md): architecture of the `Maho` library.
- [`docs/analysis.md`](docs/analysis.md): public compilation, session, and diagnostic APIs.
- [`docs/diagnostics.md`](docs/diagnostics.md): internal diagnostics model and reporters.
- [`docs/language-theory.md`](docs/language-theory.md): grammar specifications and language design.

## Build

This project uses the .NET SDK and targets `net10.0`.

Build the CLI entrypoint from the repository root:

```bash
dotnet build src/Maho.Cli/Maho.Cli.csproj
```

That also builds the core library through the project reference.

To build the library on its own:

```bash
dotnet build src/Maho/Maho.csproj
```

To run the test suite:

```bash
dotnet test
```

## CLI

Run the CLI with:

```bash
./maho [options] [source-path]
```

The `./maho` wrapper script forwards arguments to the compiled driver.

### Examples

```bash
# Compile a project file (.mhpr) with pretty diagnostics
./maho Samples/Test.mhpr

# Inspect lexer tokens as JSON
./maho --debug lex --output output/tokens.json Samples/Program.mh

# Inspect parser AST as JSON and output to stdout
./maho --debug parse --output - Samples/Program.mh

# Output diagnostics in JSON format to stderr or file
./maho --diagnostics json --output diagnostics.json Samples/Test.mhpr

# Enforce warnings as errors and full file paths
./maho -Werror --diagnostic-paths full Samples/Test.mhpr

# Check compiler version or help
./maho --version
./maho --help
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

The `Maho` library provides:

- `Compilation`: batch compilation coordinating source files, syntax trees, project references, and resolution.
  - `Compilation.FromSource(code, path, options)`
  - `Compilation.FromFiles(filePaths, projectName, options, referencedCompilations)`
  - `Compilation.FromProjectFile(projectFilePath, options)`
  - `compilation.CreateSession()`
- `AnalysisSession`: stateful incremental compilation for interactive REPLs and CLI interpreters.
  - `session.AnalyzeSnippet(code)`: parses and resolves isolated expressions, statements, or declarations against previous session symbols.
  - `session.CommitSnippet(result)`: incorporates successful declarations into the active session scope.
- `TerminalDiagnosticRenderer`: client-side Rust-style terminal diagnostic renderer in `Maho.Cli.Diagnostics`.