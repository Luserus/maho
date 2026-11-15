# Miryo Build Tool and Project Manager Guide

`Miryo` (compiled as the executable `miryo`) is the high-level build system, project orchestrator, and package/workspace manager for the Maho language ecosystem.

`Miryo` is completely decoupled from the core `Maho` compiler library. It operates by discovering and parsing domain-specific `.mhpr` project files, resolving project dependency graphs, and delegating actual source compilation to `mahoc` (and downstream code generators) as subprocesses.

---

## 1. Responsibilities & Architecture

```
                  ┌────────────────────────┐
                  │      miryo.config      │
                  │  (tools, paths, tmpls) │
                  └───────────┬────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                    Miryo CLI (`miryo`)                        │
│                                                               │
│  Commands: new, build, run, clean                             │
│                                                               │
│  Miryo.Build:                                                 │
│    ├── MahoProjectFileParser (parses .mhpr)                   │
│    ├── MiryoBuildSystem (discovers .mh source files)          │
│    ├── Project Graph Resolver (recursively builds references) │
│    └── Toolchain Launcher (locates and spawns `mahoc`)        │
└─────────────────────────────┬─────────────────────────────────┘
                              │ executes subprocess
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                 Maho Compiler Driver (`mahoc`)                │
│                                                               │
│  Options: --entry, --implicit-toplevel, --unsafe, --alias...  │
│  Compiles: *.mh source files (zero project-file awareness)    │
└───────────────────────────────────────────────────────────────┘
```

---

## 2. CLI Commands

### `miryo new [options] <project-name>`
Scaffolds a new project directory with `.mhpr` project configuration, initial source code, and `.gitignore`.

Options:
- `--lib`: Generates a library project instead of a binary executable project.
- `--config <path>`: Specifies a custom `miryo.config` file to load project templates from.

### `miryo build [options] [path]`
Builds a `.mhpr` project file, a `.mrws` workspace file, or a directory containing either:
1. Locates and parses the `.mrws` workspace file or `.mhpr` project file.
2. In workspace mode, computes topological execution tiers and dependency graphs across all member projects.
3. Recursively discovers all member projects (including nested `.mrws` workspaces) and referenced projects.
4. Locates `mahoc` using search paths defined in `miryo.config`.
5. Spawns `mahoc` passing source files, shared configuration (aliases, unsafe), and flags (`--entry`, `--implicit-toplevel`, `--unsafe`, `--alias`, etc.).

Options:
- `-p`, `--project <name>`: Builds only the specified project and its transitive dependencies within the workspace.
- `--all`: Builds all projects in the workspace, overriding the workspace `Default` configuration.
- `--exclude <name>`: Excludes specific projects from being built in this run.
- `--check`: Analyzes and type-checks the project or workspace without compiling down to intermediate representations or emitting artifacts.
- `--stop-at-il` / `--emit-il`: Emits intermediate language (`.mil`) representations.
- `-np`, `--no-project`: Compiles directory source files directly without requiring a `.mhpr` or `.mrws` file.
- `-o`, `--output <dir>`: Sets output artifact directory (defaults to `target/` in project or workspace root).
- `--color <mode>`: Controls ANSI colored diagnostics (`auto`, `always`, `never`).
- `--config <path>`: Path to a custom `miryo.config`.

### `miryo run [build-options] [path] [-- [runtime-args...]]`
Builds the project (or default workspace project) and runs the compiled binary:
- When executed in a workspace, runs the single default project or requires `-p <name>` if multiple projects exist.
- Arguments before `--` are passed to `miryo build`.
- Arguments after `--` are forwarded directly to the running executable.

### `miryo clean [path]`
Removes build artifact directories (`target/`, `bin/`, `obj/`) for the specified project or all projects in the workspace.

### `miryo compiler-server shutdown [--config <path>]`
Gracefully stops any active `mahoc-server` background daemons running for the user and toolchain.

---

## 3. Workspaces (`.mrws`)

Miryo workspaces group multiple projects (`.mhpr`) and nested sub-workspaces into a unified multi-project repository. Workspaces use the same DSL syntax as project files:

```
Name : "MahoToolchain";

Members : [
    "src/Core",
    "src/Cli/Cli.mhpr",
    "submodules/Tools.mrws",
    "samples/*"
];

Default : [ "Cli" ];

Exclude : [
    "samples/Broken/*"
];

TargetDirectory : "target";

Shared : {
    GlobalAliases : {
        Int : "Std.Int32",
        String : "Std.String"
    },
    GlobalUnsafeEnabled : true
};
```

### Workspace Features
- **Hierarchical Nesting**: Workspaces can list other `.mrws` files in `Members`. Child paths are automatically rebased relative to the child workspace's directory, and all projects flatten into a global topological execution DAG.
- **Cycle Detection**: Cycles among projects (`A -> B -> A`) or nested workspaces (`A.mrws -> B.mrws -> A.mrws`) are detected with informative error traces.
- **Topological Tiers**: Projects are grouped into execution tiers `[Tier 0, Tier 1, ...]`. Independent projects in earlier tiers build before downstream consumers.
- **Failure Short-Circuiting**: If a dependency in an earlier tier fails, dependent projects in later tiers are skipped automatically while unrelated projects continue building.
- **Shared Configuration**: `Shared` properties such as `GlobalAliases` and `GlobalUnsafeEnabled` are inherited by all member projects.

---

## 4. Configuration File (`miryo.config`)

Miryo uses an INI-style configuration file to configure toolchain paths, templates, and environment variables.

Miryo searches for `miryo.config` in:
1. An explicit path passed via `--config <path>`.
2. The current working directory (`./miryo.config`).
3. Next to the `miryo` executable (`<miryo-dir>/miryo.config`).
4. If not found, Miryo falls back to built-in defaults and issues an informational notice.

### Example `miryo.config`

```ini
# Miryo Toolchain Configuration

[tools]
# Arrays of search paths tried in order until an existing executable or package is found
mahoc = ["../maho/tools/0.2/mahoc", "../maho/shims/mahoc", "mahoc"]
mahoc_server = ["../maho/tools/0.2/maho-server", "../maho/shims/maho-server", "maho-server"]
mahoc_compiler = ["../maho/compiler/0.2"]
il2llvmir = ["./il2llvmir", "il2llvmir"]
llvm_opt = ["opt", "/usr/bin/opt"]
llvm_clang = ["clang", "/usr/bin/clang"]

[templates]
# Template strings support variable interpolation: {project_name}
bin_template = "EntryFile : \"src/Program.mh\";\nImplicitTopLevel : true;\n"
lib_template = "EntryFile : \"src/Lib.mh\";\nImplicitTopLevel : false;\n"
bin_source_path = "src/Program.mh"
bin_source = "println(\"Hello from Maho!\");\n"
lib_source_path = "src/Lib.mh"
lib_source = "public struct Lib;\n"
gitignore = "target/\nbin/\nobj/\n"
```

---

## 4. Project Configuration (`.mhpr`)

Each Maho project is defined by a domain-specific `.mhpr` file parsed by `Miryo.Build.MahoProjectFileParser`.

### Schema and Properties

| Field | Type | Description |
|---|---|---|
| `EntryFile` | string (optional) | Relative or absolute path to the designated entry-point file. |
| `ImplicitTopLevel` | bool | Whether top-level statements are implicitly allowed for the entry file. |
| `GlobalUnsafeEnabled` | bool | Whether unsafe operations are allowed project-wide (`--unsafe`). |
| `ProjectsReferenced` | array of strings | Relative paths to referenced `.mhpr` project files. |
| `GlobalAliases` | map of string: string | Global type aliases forwarded to compiler as `--alias <name>=<target>`. |
| `Sources` | object (optional) | Source discovery rules (`Directory`, `SourceFiles`, `ByName`). |

### Example Project File

```mhpr
EntryFile : "src/Program.mh";
ImplicitTopLevel : true;
GlobalUnsafeEnabled : false;
ProjectsReferenced : [
    "../StdLib/Std.Core.mhpr"
];
GlobalAliases : {
    "int32" : "Std.Int32",
    "str8" : "Std.String8"
};
Sources : {
    Directory : "src",
    ByName : "*.mh"
};
```
