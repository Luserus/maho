using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Miryo;

/// <summary>
/// Represents the parsed INI-style configuration for Miryo build system,
/// controlling toolchain binary paths, template definitions, and variables.
/// </summary>
public sealed class MiryoConfiguration
{
    private readonly Dictionary<string, Dictionary<string, ConfigValue>> sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary> Directory where the configuration file was loaded from, or base directory. </summary>
    public string ConfigDirectory { get; }

    /// <summary> Explicit file path if loaded from disk, or null. </summary>
    public string? ConfigFilePath { get; }

    /// <summary> Whether this configuration was loaded from an existing file on disk. </summary>
    public bool IsLoadedFromDisk { get; }

    /// <summary> Informative warning message if a configuration file was not found and defaults are being used. </summary>
    public string? LoadWarning { get; }

    public MiryoConfiguration(
        string configDirectory,
        string? configFilePath = null,
        bool isLoadedFromDisk = false,
        string? loadWarning = null)
    {
        ConfigDirectory = configDirectory;
        ConfigFilePath = configFilePath;
        IsLoadedFromDisk = isLoadedFromDisk;
        LoadWarning = loadWarning;
    }

    /// <summary> Creates the built-in default configuration. </summary>
    public static MiryoConfiguration CreateDefault(
        string? baseDir = null,
        bool isLoadedFromDisk = false,
        string? loadWarning = null)
    {
        string dir = baseDir ?? AppContext.BaseDirectory;
        var config = new MiryoConfiguration(dir, configFilePath: null, isLoadedFromDisk: isLoadedFromDisk, loadWarning: loadWarning);

        config.SetRaw("tools", "mahoc", new ConfigValue.ArrayValue(["./mahoc", "./dist/mahoc", "mahoc"]));
        config.SetRaw("tools", "il2llvmir", new ConfigValue.ArrayValue(["./il2llvmir", "./dist/il2llvmir", "il2llvmir"]));
        config.SetRaw("tools", "llvm_opt", new ConfigValue.ArrayValue(["opt", "/usr/bin/opt"]));
        config.SetRaw("tools", "llvm_clang", new ConfigValue.ArrayValue(["clang", "/usr/bin/clang"]));

        config.SetRaw("templates", "bin_template", new ConfigValue.StringLiteral("EntryFile : \"src/Program.mh\";\nImplicitTopLevel : true;\n"));
        config.SetRaw("templates", "lib_template", new ConfigValue.StringLiteral("EntryFile : \"src/Lib.mh\";\nImplicitTopLevel : false;\n"));
        config.SetRaw("templates", "bin_source_path", new ConfigValue.StringLiteral("src/Program.mh"));
        config.SetRaw("templates", "bin_source", new ConfigValue.StringLiteral("println(\"Hello from Maho!\");\n"));
        config.SetRaw("templates", "lib_source_path", new ConfigValue.StringLiteral("src/Lib.mh"));
        config.SetRaw("templates", "lib_source", new ConfigValue.StringLiteral("public struct Lib;\n"));
        config.SetRaw("templates", "gitignore", new ConfigValue.StringLiteral("target/\nbin/\nobj/\ndist/\n"));

        return config;
    }

    /// <summary>
    /// Loads configuration by probing:
    /// 1. explicitConfigPath (if provided)
    /// 2. miryo.config relative to the Miryo executable
    /// 3. miryo.config in current working directory
    /// 4. Falls back to built-in default configuration with an informative message.
    /// </summary>
    public static MiryoConfiguration Load(string? explicitConfigPath = null)
    {
        string exeDir = AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            string fullPath = Path.GetFullPath(explicitConfigPath);
            if (File.Exists(fullPath))
            {
                return Parse(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!, fullPath, isLoadedFromDisk: true);
            }

            return CreateDefault(
                exeDir,
                isLoadedFromDisk: false,
                loadWarning: $"Configuration file '{explicitConfigPath}' not found. Using default built-in template.");
        }

        string exeConfig = Path.Combine(exeDir, "miryo.config");
        if (File.Exists(exeConfig))
            return Parse(File.ReadAllText(exeConfig), exeDir, exeConfig, isLoadedFromDisk: true);

        string cwd = Directory.GetCurrentDirectory();
        string cwdConfig = Path.Combine(cwd, "miryo.config");
        if (File.Exists(cwdConfig))
            return Parse(File.ReadAllText(cwdConfig), cwd, cwdConfig, isLoadedFromDisk: true);

        return CreateDefault(
            exeDir,
            isLoadedFromDisk: false,
            loadWarning: "Configuration file not found. Using default built-in template.");
    }

    /// <summary> Parses INI-style configuration text. </summary>
    public static MiryoConfiguration Parse(string content, string configDirectory, string? configFilePath = null, bool isLoadedFromDisk = true)
    {
        var config = new MiryoConfiguration(configDirectory, configFilePath, isLoadedFromDisk: isLoadedFromDisk);
        var defaults = CreateDefault(configDirectory, isLoadedFromDisk: isLoadedFromDisk);
        foreach (var (secName, secValues) in defaults.sections)
        {
            foreach (var (k, v) in secValues)
                config.SetRaw(secName, k, v);
        }

        var parser = new ConfigParser(content);
        parser.ParseInto(config);
        return config;
    }

    public void SetRaw(string section, string key, ConfigValue value)
    {
        if (!sections.TryGetValue(section, out var dict))
        {
            dict = new Dictionary<string, ConfigValue>(StringComparer.OrdinalIgnoreCase);
            sections[section] = dict;
        }
        dict[key] = value;
    }

    public ConfigValue? GetRaw(string section, string key)
    {
        if (sections.TryGetValue(section, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        return null;
    }

    /// <summary> Returns tool candidate paths for the specified tool name. </summary>
    public IReadOnlyList<string> GetToolCandidates(string toolName, IReadOnlyDictionary<string, string>? variables = null)
    {
        var val = GetRaw("tools", toolName);
        if (val is null)
            return [];

        return val.EvaluateList(variables ?? new Dictionary<string, string>());
    }

    /// <summary>
    /// Searches for an existing executable for the given tool, checking candidates in order.
    /// Returns the resolved path or null if none found.
    /// </summary>
    public string? FindTool(string toolName, IReadOnlyDictionary<string, string>? variables = null)
    {
        var candidates = GetToolCandidates(toolName, variables);
        foreach (string candidate in candidates)
        {
            string expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (TryResolveExecutable(expanded, out string? resolved))
                return resolved;
        }

        return null;
    }

    /// <summary>
    /// Evaluates a template string with variable substitutions (e.g. project_name).
    /// </summary>
    public string GetTemplate(string templateName, IReadOnlyDictionary<string, string>? variables = null)
    {
        var val = GetRaw("templates", templateName);
        if (val is null)
        {
            if (templateName == "bin_source") val = GetRaw("templates", "bin_source_template");
            else if (templateName == "lib_source") val = GetRaw("templates", "lib_source_template");
            else if (templateName == "gitignore") val = GetRaw("templates", "gitignore_template");
        }

        if (val is null)
        {
            return templateName switch
            {
                "lib_template" => "EntryFile : \"src/Lib.mh\";\nImplicitTopLevel : false;\n",
                "bin_template" => "EntryFile : \"src/Program.mh\";\nImplicitTopLevel : true;\n",
                "bin_source_path" => "src/Program.mh",
                "bin_source" or "bin_source_template" => "println(\"Hello from Maho!\");\n",
                "lib_source_path" => "src/Lib.mh",
                "lib_source" or "lib_source_template" => "public struct Lib;\n",
                "gitignore" or "gitignore_template" => "target/\nbin/\nobj/\ndist/\n",
                _ => string.Empty
            };
        }

        return val.EvaluateString(variables ?? new Dictionary<string, string>());
    }

    private bool TryResolveExecutable(string candidate, out string? resolvedPath)
    {
        bool isWindows = OperatingSystem.IsWindows();

        // 1. Relative path starting with ./ or ../ or directory separator
        if (candidate.StartsWith("./", StringComparison.Ordinal) ||
            candidate.StartsWith(".\\", StringComparison.Ordinal) ||
            candidate.StartsWith("../", StringComparison.Ordinal) ||
            candidate.StartsWith("..\\", StringComparison.Ordinal))
        {
            string fullPath = Path.GetFullPath(Path.Combine(ConfigDirectory, candidate));
            if (FileExistsWithExt(fullPath, isWindows, out resolvedPath))
                return true;
        }

        // 2. Absolute or rooted path
        if (Path.IsPathRooted(candidate))
        {
            if (FileExistsWithExt(candidate, isWindows, out resolvedPath))
                return true;
        }

        // 3. Relative path without leading ./
        string localPath = Path.GetFullPath(Path.Combine(ConfigDirectory, candidate));
        if (FileExistsWithExt(localPath, isWindows, out resolvedPath))
            return true;

        // 4. PATH lookup for bare commands
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var separator = isWindows ? ';' : ':';
            foreach (string entry in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
            {
                string combined = Path.Combine(entry, candidate);
                if (FileExistsWithExt(combined, isWindows, out resolvedPath))
                    return true;
            }
        }

        resolvedPath = null;
        return false;
    }

    /// <summary>
    /// Creates a ProcessStartInfo configured to execute the tool, automatically using 'dotnet' host if tool is a managed .dll.
    /// </summary>
    public static System.Diagnostics.ProcessStartInfo CreateProcessStartInfo(string toolExecutablePath, IEnumerable<string> arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        if (toolExecutablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(toolExecutablePath);
        }
        else
        {
            psi.FileName = toolExecutablePath;
        }

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        return psi;
    }

    private static bool FileExistsWithExt(string path, bool isWindows, out string? resolved)
    {
        if (File.Exists(path))
        {
            resolved = Path.GetFullPath(path);
            return true;
        }

        if (isWindows && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string exe = path + ".exe";
            if (File.Exists(exe))
            {
                resolved = Path.GetFullPath(exe);
                return true;
            }
        }

        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            string dll = path + ".dll";
            if (File.Exists(dll))
            {
                resolved = Path.GetFullPath(dll);
                return true;
            }
        }

        resolved = null;
        return false;
    }

    public abstract record ConfigValue
    {
        public abstract string EvaluateString(IReadOnlyDictionary<string, string> variables);
        public abstract IReadOnlyList<string> EvaluateList(IReadOnlyDictionary<string, string> variables);

        public sealed record StringLiteral(string Value) : ConfigValue
        {
            public override string EvaluateString(IReadOnlyDictionary<string, string> variables) =>
                EvaluateInterpolation(Value, variables);

            public override IReadOnlyList<string> EvaluateList(IReadOnlyDictionary<string, string> variables) =>
                [EvaluateString(variables)];
        }

        public sealed record VariableReference(string Name) : ConfigValue
        {
            public override string EvaluateString(IReadOnlyDictionary<string, string> variables) =>
                variables.TryGetValue(Name, out string? val) ? val : $"${{{Name}}}";

            public override IReadOnlyList<string> EvaluateList(IReadOnlyDictionary<string, string> variables) =>
                [EvaluateString(variables)];
        }

        public sealed record Concatenation(IReadOnlyList<ConfigValue> Parts) : ConfigValue
        {
            public override string EvaluateString(IReadOnlyDictionary<string, string> variables)
            {
                var sb = new StringBuilder();
                foreach (var part in Parts)
                    sb.Append(part.EvaluateString(variables));
                return sb.ToString();
            }

            public override IReadOnlyList<string> EvaluateList(IReadOnlyDictionary<string, string> variables) =>
                [EvaluateString(variables)];
        }

        public sealed record ArrayValue(IReadOnlyList<ConfigValue> Items) : ConfigValue
        {
            public ArrayValue(IEnumerable<string> literals)
                : this(literals.Select(l => (ConfigValue)new StringLiteral(l)).ToList())
            {
            }

            public override string EvaluateString(IReadOnlyDictionary<string, string> variables) =>
                Items.Count > 0 ? Items[0].EvaluateString(variables) : string.Empty;

            public override IReadOnlyList<string> EvaluateList(IReadOnlyDictionary<string, string> variables) =>
                Items.Select(item => item.EvaluateString(variables)).ToList();
        }

        protected static string EvaluateInterpolation(string template, IReadOnlyDictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            var sb = new StringBuilder(template.Length);
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] == '$' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    int close = template.IndexOf('}', i + 2);
                    if (close > i + 1)
                    {
                        string varName = template.Substring(i + 2, close - (i + 2));
                        if (variables.TryGetValue(varName, out string? val))
                            sb.Append(val);
                        else
                            sb.Append("${").Append(varName).Append('}');
                        i = close;
                        continue;
                    }
                }
                else if (template[i] == '{' && i + 1 < template.Length)
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close > i + 1)
                    {
                        string varName = template.Substring(i + 1, close - (i + 1));
                        if (variables.TryGetValue(varName, out string? val))
                        {
                            sb.Append(val);
                            i = close;
                            continue;
                        }
                    }
                }

                sb.Append(template[i]);
            }

            return sb.ToString();
        }
    }

    private sealed class ConfigParser
    {
        private readonly string text;
        private int current;

        public ConfigParser(string text) => this.text = text;

        public void ParseInto(MiryoConfiguration config)
        {
            string currentSection = "general";

            while (!IsAtEnd)
            {
                SkipWhitespaceAndComments();
                if (IsAtEnd)
                    break;

                char ch = Peek();
                if (ch == '[')
                {
                    currentSection = ParseSectionHeader();
                    continue;
                }

                // Parse key = value
                string key = ParseIdentifier();
                SkipWhitespaceOnly();
                if (!Match('='))
                    throw new FormatException($"Expected '=' after key '{key}' at position {current}.");

                SkipWhitespaceOnly();
                var value = ParseExpression();
                config.SetRaw(currentSection, key, value);

                SkipWhitespaceAndComments();
            }
        }

        private string ParseSectionHeader()
        {
            Advance(); // skip '['
            SkipWhitespaceOnly();
            string name = ParseIdentifier();
            SkipWhitespaceOnly();
            if (!Match(']'))
                throw new FormatException($"Expected ']' after section name '{name}' at position {current}.");
            return name;
        }

        private ConfigValue ParseExpression()
        {
            SkipWhitespaceOnly();

            if (Match('['))
                return ParseArray();

            var parts = new List<ConfigValue>();
            parts.Add(ParsePrimary());

            while (true)
            {
                SkipWhitespaceOnly();
                if (Match('+'))
                {
                    SkipWhitespaceOnly();
                    parts.Add(ParsePrimary());
                }
                else
                {
                    break;
                }
            }

            return parts.Count == 1 ? parts[0] : new ConfigValue.Concatenation(parts);
        }

        private ConfigValue ParseArray()
        {
            var items = new List<ConfigValue>();
            SkipWhitespaceAndComments();

            if (Match(']'))
                return new ConfigValue.ArrayValue(items);

            while (!IsAtEnd)
            {
                SkipWhitespaceAndComments();
                items.Add(ParseExpression());
                SkipWhitespaceAndComments();

                if (Match(','))
                    continue;

                if (Match(']'))
                    break;

                throw new FormatException($"Expected ',' or ']' in array at position {current}.");
            }

            return new ConfigValue.ArrayValue(items);
        }

        private ConfigValue ParsePrimary()
        {
            char ch = Peek();
            if (ch is '"' or '\'')
                return new ConfigValue.StringLiteral(ParseStringLiteral());

            if (char.IsLetter(ch) || ch == '_')
            {
                string ident = ParseIdentifier();
                return new ConfigValue.VariableReference(ident);
            }

            throw new FormatException($"Unexpected character '{ch}' at position {current} while parsing config value.");
        }

        private string ParseStringLiteral()
        {
            char quote = Advance();
            var sb = new StringBuilder();

            while (!IsAtEnd)
            {
                char ch = Advance();
                if (ch == quote)
                    return sb.ToString();

                if (ch == '\\' && !IsAtEnd)
                {
                    char esc = Advance();
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }

            throw new FormatException("Unterminated string literal in configuration.");
        }

        private string ParseIdentifier()
        {
            int start = current;
            while (!IsAtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_' || Peek() == '-'))
                Advance();

            if (start == current)
                throw new FormatException($"Expected identifier at position {current}.");

            return text.Substring(start, current - start);
        }

        private void SkipWhitespaceAndComments()
        {
            while (!IsAtEnd)
            {
                char ch = Peek();
                if (char.IsWhiteSpace(ch))
                {
                    Advance();
                }
                else if (ch is '#' or ';')
                {
                    while (!IsAtEnd && Peek() != '\n')
                        Advance();
                }
                else
                {
                    break;
                }
            }
        }

        private void SkipWhitespaceOnly()
        {
            while (!IsAtEnd && (Peek() == ' ' || Peek() == '\t'))
                Advance();
        }

        private bool IsAtEnd => current >= text.Length;
        private char Peek() => IsAtEnd ? '\0' : text[current];
        private char Advance() => text[current++];
        private bool Match(char expected)
        {
            if (Peek() == expected)
            {
                Advance();
                return true;
            }
            return false;
        }
    }
}
