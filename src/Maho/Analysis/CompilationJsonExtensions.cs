using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Maho.Syntax;

namespace Maho;

/// <summary>
/// Provides JSON serialization abstractions for Maho compilation artifacts,
/// syntax trees, and diagnostics.
/// </summary>
public static class CompilationJsonExtensions
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes the <see cref="Compilation"/> into a JSON string.
    /// </summary>
    public static string ToJson(this Compilation compilation, JsonSerializerOptions? options = null)
    {
        options ??= DefaultJsonOptions;
        var node = compilation.ToJsonNode();
        return node.ToJsonString(options);
    }

    /// <summary>
    /// Converts the <see cref="Compilation"/> into a structured <see cref="JsonNode"/>.
    /// </summary>
    public static JsonNode ToJsonNode(this Compilation compilation)
    {
        var filesArray = new JsonArray();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var fileObj = new JsonObject
            {
                ["filePath"] = tree.FilePath,
                ["hasErrors"] = tree.HasErrors,
                ["diagnostics"] = JsonSerializer.SerializeToNode(tree.Diagnostics, DefaultJsonOptions)
            };
            filesArray.Add(fileObj);
        }

        foreach (var (path, (errorMsg, isInternal)) in compilation.FileErrors)
        {
            var errObj = new JsonObject
            {
                ["filePath"] = path,
                ["hasErrors"] = true,
                ["analysisError"] = errorMsg,
                ["isInternalError"] = isInternal
            };
            filesArray.Add(errObj);
        }

        return new JsonObject
        {
            ["projectName"] = compilation.ProjectName,
            ["entryFile"] = compilation.EntryFile,
            ["hasErrors"] = compilation.HasErrors,
            ["elapsedTicks"] = compilation.Elapsed.Ticks,
            ["files"] = filesArray,
            ["diagnostics"] = JsonSerializer.SerializeToNode(compilation.Diagnostics, DefaultJsonOptions)
        };
    }

    /// <summary>
    /// Serializes a <see cref="SyntaxTree"/> into a JSON string.
    /// </summary>
    public static string ToJson(this SyntaxTree syntaxTree, JsonSerializerOptions? options = null)
    {
        options ??= DefaultJsonOptions;
        var node = syntaxTree.ToJsonNode();
        return node.ToJsonString(options);
    }

    /// <summary>
    /// Converts a <see cref="SyntaxTree"/> into a structured <see cref="JsonNode"/>.
    /// </summary>
    public static JsonNode ToJsonNode(this SyntaxTree syntaxTree)
    {
        return new JsonObject
        {
            ["filePath"] = syntaxTree.FilePath,
            ["hasErrors"] = syntaxTree.HasErrors,
            ["diagnostics"] = JsonSerializer.SerializeToNode(syntaxTree.Diagnostics, DefaultJsonOptions)
        };
    }

    /// <summary>
    /// Serializes a <see cref="DiagnosticInfo"/> into a JSON string.
    /// </summary>
    public static string ToJson(this DiagnosticInfo diagnostic, JsonSerializerOptions? options = null)
    {
        options ??= DefaultJsonOptions;
        return JsonSerializer.Serialize(diagnostic, options);
    }
}
