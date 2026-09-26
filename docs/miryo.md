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
Builds a `.mhpr` project file or a project in the specified directory:
1. Locates and parses the `.mhpr` project file.
2. Recursively builds any referenced projects declared in `ProjectsReferenced`.
3. Resolves all source files matching the project's discovery rules.
4. Locates `mahoc` using search paths defined in `miryo.config`.
5. Spawns `mahoc` passing source files and flags (`--entry`, `--implicit-toplevel`, `--unsafe`, `--alias`, etc.).

Options:
- `--check`: Analyzes and type-checks the project without compiling down to intermediate representations.
- `--emit-il`: Emits intermediate language (`.mil`) representations.
- `-o`, `--output-dir <dir>`: Sets output artifact directory (defaults to `target/`).
- `--config <path>`: Path to a custom `miryo.config`.

### `miryo run [build-options] [path] [-- [runtime-args...]]`
Builds the project and runs the compiled binary:
- Arguments before `--` are passed to `miryo build`.
- Arguments after `--` are forwarded directly to the running executable.

### `miryo clean [path]`
Removes the `target/` build output directory for the specified project.

---

## 3. Configuration File (`miryo.config`)

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
# Arrays of search paths tried in order until an existing executable is found
mahoc = ["./dist/mahoc", "./src/MahoCli/bin/Debug/net10.0/mahoc.dll", "mahoc"]
il2llvmir = ["./dist/il2llvmir", "il2llvmir"]
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
gitignore = "target/\nbin/\nobj/\ndist/\n"
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
