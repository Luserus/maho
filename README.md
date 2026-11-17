# Maho

The compiler and toolchain for the Maho programming language.

[![.NET](https://github.com/Luserus/maho/actions/workflows/dotnet.yml/badge.svg?branch=develop)](https://github.com/Luserus/maho/actions/workflows/dotnet.yml)

---

## 1. Compiler Overview & Capabilities

Maho's compiler frontend is fully implemented and passes comprehensive static semantic verification across the language's grammar, type system, and control flow:

- **Source Modeling & Lexing**: UTF-8 line indexing and span arithmetic via `SourceText`; complete tokenization preserving leading and trailing syntax trivia; custom numeric suffixes (`SuffixedInteger`, `SuffixedFloat`, `SuffixedChar`, `SuffixedString`).
- **Pratt Parsing**: Recursive descent parser with Pratt operator-precedence climbing; unified loop/branch expressions (`WhileExpression`, `IfExpression`); lambdas and block expressions; turbofish specialization (`::`); collection expressions (`[...] with (...)`); explicit receiver methods (`&this`, `mut &this`, `*this`, `mut *this`, `this*`, `this mut*`, `this&`, `mut this&`, `this`).
- **Type Modifiers & Aliasing**: Prefix safe/unsafe reference aliases (`&T`, `*T`, `mut &T`, `mut *T`) with mandatory declaration initialization; postfix references (`Type&`, `Type mut&`), pointers (`Type*`, `Type mut*`), spans (`Type[&]`), arrays (`Type[]`, `Type[N]`), and optionals (`Type?`).
- **Macro Expansion Pass (`MacroExpansionPass`)**: Syntactic macro definitions and expansions with hygienic token tracking (`HygieneId`), recursion limits, and tree-rendered diagnostic tracebacks.
- **Symbol & Scope Discovery (`SymbolDiscoveryPass`)**: Namespaces with hierarchical trie lookup; types, functions, methods, properties, fields, and aliases; partial merging for types and functions; block scope directives (`global { ... }`, `const { ... }`); opt-in top-level statement synthesis.
- **Intrinsic Verification & ABI (`IntrinsicDiscoveryPass`)**: Discovers compiler attributes (`[IntrinsicType]`, `[OperatorFunction]`, `[IntrinsicFunction]`); validates topological memory layouts and field counts on primitive scalars (`Int8`..`Int128`, `UInt8`..`UInt128`, `Float16`..`Float128`, `Bool`, `Char`), bottom type `Never`, callables (`Function`, `Delegate`), pointers/references (`Pointer`, `MutablePointer`, `Reference`, `MutableReference`), and containers (`Array`, `FixedArray`, `Span`, `Str`, `Tuple`, `Optional`); catalogs operator interfaces.
- **Declaration Resolution & C3 Linearization (`DeclarationResolutionPass`)**: Type reference binding; inheritance graph resolution with C3 linearization across multiple shared bases and non-contiguous split bases; cyclic inheritance detection; strict receiver-kind matching for interface conformance.
- **Expression & Operator Resolution (`ExpressionResolutionPass`)**: Universal L-value evaluation (any expression yielding `mut &T` or `mut *T` is assignable); sequential eager collapsing with retroactive literal resolution; zero frontend constant folding; two-phase allocation and construction (`put`/`new`); receiver mutability checking (`Deref` vs `MutDeref`, `Arrow` vs `MutArrow`); canonical descriptive operator interfaces (`Addition`, `Subtraction`, `Multiplication`, `Division`, `Remainder`, `BitwiseAnd`, `BitwiseOr`, `BitwiseXor`, `LeftShift`, `RightShift`, `Equality`, `Inequality`, `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual`, `UnaryNegation`, `UnaryPositive`, `BitwiseNot`); sum type exhaustiveness and pattern matching.
- **Control Flow & Return Inference (`ControlFlowAndReturnInferencePass`)**: Basic-block control flow graph (CFG) construction; dead code and reachability analysis; return type inference; diverging function verification.
- **Compilation Boundary**: Full static analysis, type checking, and verification complete cleanly (`--check`). Emits `MH9000` at the lowering boundary until the backend intermediate language (MIL) code generator is connected.

---

## 2. Building the Project

### Prerequisites
- **.NET 10 SDK** (`net10.0`) installed on your system.

### Running Tests
The repository contains 787 automated unit tests covering syntax, semantics, types, intrinsics, macros, diagnostics, and tooling:

```bash
dotnet test
```

### Building with `dotnet`
To build the solution:

```bash
dotnet build maho.sln
```

### Building the Toolchain Binaries
Build the compiler driver (`mahoc`) and project manager (`miryo`) using the build scripts:

```bash
# Linux / macOS / WSL
./build-maho

# Windows PowerShell
.\build-maho.ps1

# Windows Command Prompt
build-maho
```

#### Build Script Flags
- `./build-maho` (default): Clean release build for host platform into `bin/`.
- `./build-maho --single-file` (or `-s`): Bundles managed assemblies and runtime configuration into standalone executables with shared runtimes.
- `./build-maho --debug` (or `-d`): Builds Debug configuration, emitting `.pdb` symbols and `.deps.json`.
- `./build-maho --all`: Cross-compiles for all major platforms (`linux-x64`, `linux-arm64`, `win-x64`, `osx-x64`, `osx-arm64`) into `bin/<platform>/`.

---

## 3. Toolchain Output Layout (`bin/`)

When built, the toolchain is organized into clean, modular directory trees under `bin/`:

```text
bin/
├── miryo/                         # Project manager & build orchestrator
│   ├── miryo                      # Project manager host executable
│   ├── miryo-update               # Toolchain update utility
│   ├── miryo.config               # Default configuration (search paths & templates)
│   ├── manifest.json              # Tool manifest metadata
│   └── runtime/                   # Shared host .NET runtime
└── maho/                          # Compiler toolchain & daemon
    ├── tools/<version>/           # Versioned toolchain binaries
    │   ├── mahoc                  # Compiler driver executable
    │   ├── maho-server            # Compiler daemon executable
    │   ├── maho-ls                # Language server executable shim
    │   └── manifest.json          # Package manifest
    ├── compiler/<version>/        # Versioned compiler assembly package
    │   ├── Maho.dll               # Core compiler engine
    │   └── manifest.json          # Compiler metadata
    ├── server/                    # Persistent compiler server manager
    │   ├── server-manager         # Daemon lifecycle supervisor
    │   ├── MahoServer.dll         # IPC protocol & session manager
    │   └── manifest.json          # Daemon manifest
    ├── shims/                     # PATH-friendly launch shims
    │   ├── mahoc                  # Symlink/shim to active compiler
    │   ├── maho-server            # Symlink/shim to active server
    │   └── maho-ls                # Symlink/shim to active language server
    └── runtime/                   # Shared .NET host runtime
```

---

## 4. How to Use the Tools

### 4.1 Project Manager: `miryo`

`miryo` is the high-level build system, project orchestrator, and package/workspace manager.

```bash
# Create a new binary executable project
./bin/miryo/miryo new MyApp

# Create a new library project
./bin/miryo/miryo new --lib MyLib

# Build a project or workspace
./bin/miryo/miryo build

# Type-check without code generation
./bin/miryo/miryo build --check

# Build a specific project within a workspace
./bin/miryo/miryo build -p Core

# Run a project (forwarding arguments after --)
./bin/miryo/miryo run -- --verbose arg1

# Clean build artifacts (target/, bin/, obj/)
./bin/miryo/miryo clean

# Gracefully shut down background compiler daemons
./bin/miryo/miryo compiler-server shutdown
```

### 4.2 Compiler Driver: `mahoc`

`mahoc` is the command-line driver operating directly on `.mh` source files and directories without requiring project files.

```bash
# Type-check a single source file
./bin/maho/shims/mahoc --check src/Program.mh
# Or using the versioned binary directly:
./bin/maho/tools/0.2/mahoc --check src/Program.mh

# Type-check an entire directory
./bin/maho/tools/0.2/mahoc --check src/

# Specify an explicit entry point file
./bin/maho/tools/0.2/mahoc --check --entry src/Main.mh src/

# Pass global type aliases
./bin/maho/tools/0.2/mahoc --check --alias int32=Std.Int32 src/

# Enable global unsafe operations
./bin/maho/tools/0.2/mahoc --check --unsafe src/

# Output machine-readable JSON diagnostics to stdout
./bin/maho/tools/0.2/mahoc --diagnostics json -o - src/

# Inspect AST or tokens as JSON
./bin/maho/tools/0.2/mahoc --debug parse -o - src/Program.mh
```

### 4.3 Project and Workspace Configuration

#### Project File (`.mhpr`)
Projects are configured with declarative `.mhpr` files:

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

#### Workspace File (`.mrws`)
Workspaces group multiple projects into unified multi-project builds with topological dependency scheduling:

```mrws
Name : "MyWorkspace";
Members : [
    "src/Core",
    "src/Cli/Cli.mhpr",
    "samples/*"
];
Default : [ "Cli" ];
Exclude : [ "samples/Experimental/*" ];
```

---

## 5. Documentation Map

Detailed subsystem guides are located in [`docs/`](docs/):

- [`docs/repository-guide.md`](docs/repository-guide.md): Map of repository subsystems and reading strategy.
- [`docs/cli.md`](docs/cli.md): `mahoc` compiler CLI options, driver workflows, and diagnostic routing.
- [`docs/miryo.md`](docs/miryo.md): Build tool, `.mhpr` project files, `.mrws` workspaces, and `miryo.config`.
- [`docs/compiler-library.md`](docs/compiler-library.md): Architecture of the core `Maho` library and resolution passes.
- [`docs/language-theory.md`](docs/language-theory.md): Formal grammar specifications, Pratt operator precedence, and resolution semantics.
- [`docs/diagnostic-codes.md`](docs/diagnostic-codes.md): Complete diagnostic code registry (`MH0001` - `MH9001`).
- [`docs/source-tree.md`](docs/source-tree.md): Repository project layout and test suite structure.
- [`docs/analysis.md`](docs/analysis.md): Public compilation, session, phase timers, and diagnostic APIs.
- [`docs/diagnostics.md`](docs/diagnostics.md): Diagnostic engine and terminal rendering architecture.
- [`docs/source-text.md`](docs/source-text.md): UTF-8 text model, line indexing, and span arithmetic.
- [`docs/syntax.md`](docs/syntax.md): Syntax tree structure and AST category guides:
  - [`docs/syntax-declarations.md`](docs/syntax-declarations.md)
  - [`docs/syntax-expressions.md`](docs/syntax-expressions.md)
  - [`docs/syntax-fragments.md`](docs/syntax-fragments.md)
  - [`docs/syntax-statements.md`](docs/syntax-statements.md)
