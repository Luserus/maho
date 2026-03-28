# Maho

An experimental programming language and compiler project inspired by C#.

## Build

This project is built with the .NET SDK and currently targets `net10.0`.

From the repository root, run:

```bash
dotnet build src/Maho/Maho.sln
```

You can also build the project directly:

```bash
dotnet build src/Maho/Maho.csproj
```

At the moment, the build is not fully passing because [src/Maho/Resolution/Scope.cs](/home/luserus/SoftwareDev/Systems/Compiler/maho/src/Maho/Resolution/Scope.cs) references missing symbol types (`TypeSymbol` and `IValueSymbol`). The commands above are still the correct way to build once those types are added or the references are updated.
