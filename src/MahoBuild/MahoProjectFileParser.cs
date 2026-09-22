using System;
using System.Collections.Generic;
using System.Text;

namespace Maho.Build;

/// <summary> Parses the compiler's domain-specific, JSON-inspired <c>.mhpr</c> format. </summary>
public sealed class MahoProjectFileParser
{
    private readonly string text;
    private int current;

    private MahoProjectFileParser(string text) => this.text = text;

    public static MahoProjectConfiguration Parse(string text)
    {
        var parser = new MahoProjectFileParser(text);
        return parser.ParseConfiguration();
    }

    private MahoProjectConfiguration ParseConfiguration()
    {
        string? entryFile = null;
        bool globalUnsafeEnabled = false;
        bool implicitTopLevel = false;
        string[] projectsReferenced = [];
        Dictionary<string, string> globalAliases = [];
        MahoProjectSourcesConfiguration? sources = null;
        HashSet<string> seenProperties = [];

        SkipWhitespace();

        while (!IsAtEnd)
        {
            string name = ParseIdentifier("for the project property name");

            if (!seenProperties.Add(name))
                throw Error($"Project property '{name}' cannot be specified more than once.");

            SkipWhitespace();
            Expect(':', $"after project property '{name}'");
            SkipWhitespace();

            switch (name)
            {
                case "EntryFile":
                    entryFile = ParseString("for EntryFile");
                    break;
                case "GlobalUnsafeEnabled":
                    globalUnsafeEnabled = ParseBoolean("for GlobalUnsafeEnabled");
                    break;
                case "ImplicitTopLevel":
                case "AllowImplicitTopLevel":
                    implicitTopLevel = ParseBoolean($"for {name}");
                    break;
                case "ProjectsReferenced":
                    projectsReferenced = ParseStringArray("for ProjectsReferenced");
                    break;
                case "GlobalAliases":
                    globalAliases = ParseStringMap("for GlobalAliases");
                    break;
                case "Sources":
                    sources = ParseSourcesConfiguration("for Sources");
                    break;
                default:
                    throw Error($"Unknown project property '{name}'.");
            }

            SkipWhitespace();
            Expect(';', $"after project property '{name}'");
            SkipWhitespace();
        }

        return new MahoProjectConfiguration
        {
            EntryFile = entryFile,
            GlobalUnsafeEnabled = globalUnsafeEnabled,
            ImplicitTopLevel = implicitTopLevel,
            ProjectsReferenced = projectsReferenced,
            GlobalAliases = globalAliases,
            Sources = sources
        };
    }

    private MahoProjectSourcesConfiguration ParseSourcesConfiguration(string context)
    {
        SkipWhitespace();
        if (CurrentChar is '[')
        {
            return new MahoProjectSourcesConfiguration
            {
                SourceFiles = ParseStringArray(context)
            };
        }

        Expect('{', context);
        SkipWhitespace();

        string? directory = null;
        string[] sourceFiles = [];
        string? byName = null;
        HashSet<string> seenProperties = [];

        while (CurrentChar is not '}')
        {
            string key = CurrentChar is '"'
                ? ParseString("for Sources property name")
                : ParseIdentifier("for Sources property name");

            if (!seenProperties.Add(key))
                throw Error($"Property '{key}' cannot be specified more than once in Sources.");

            SkipWhitespace();
            Expect(':', $"after Sources property '{key}'");
            SkipWhitespace();

            switch (key)
            {
                case "Directory":
                    directory = ParseString("for Directory in Sources");
                    break;
                case "SourceFiles":
                    sourceFiles = ParseStringArray("for SourceFiles in Sources");
                    break;
                case "ByName":
                    byName = ParseString("for ByName in Sources");
                    break;
                default:
                    throw Error($"Unknown property '{key}' in Sources.");
            }

            SkipWhitespace();

            if (CurrentChar is ',')
            {
                current++;
                SkipWhitespace();
                if (CurrentChar is '}')
                    break;
            }
            else
            {
                break;
            }
        }

        Expect('}', context);

        return new MahoProjectSourcesConfiguration
        {
            Directory = directory,
            SourceFiles = sourceFiles,
            ByName = byName
        };
    }

    private string[] ParseStringArray(string context)
    {
        Expect('[', context);
        SkipWhitespace();
        List<string> values = [];

        while (CurrentChar is not ']')
        {
            values.Add(ParseString(context));
            SkipWhitespace();

            if (CurrentChar is ',')
            {
                current++;
                SkipWhitespace();
                if (CurrentChar is ']')
                    break;
            }
            else
            {
                break;
            }
        }

        Expect(']', context);
        return [.. values];
    }

    private Dictionary<string, string> ParseStringMap(string context)
    {
        Expect('{', context);
        SkipWhitespace();
        Dictionary<string, string> values = [];

        while (CurrentChar is not '}')
        {
            string key = ParseString(context);
            SkipWhitespace();
            Expect(':', $"after alias '{key}'");
            SkipWhitespace();

            if (!values.TryAdd(key, ParseString(context)))
                throw Error($"Alias '{key}' cannot be specified more than once.");

            SkipWhitespace();

            if (CurrentChar is ',')
            {
                current++;
                SkipWhitespace();
                if (CurrentChar is '}')
                    break;
            }
            else
            {
                break;
            }
        }

        Expect('}', context);
        return values;
    }

    private bool ParseBoolean(string context)
    {
        string value = ParseIdentifier(context);

        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw Error($"Expected 'true' or 'false' {context}.")
        };
    }

    private string ParseString(string context)
    {
        Expect('"', context);
        var value = new StringBuilder();

        while (true)
        {
            if (IsAtEnd)
                throw Error($"Unterminated string {context}.");

            char character = text[current++];

            if (character is '"')
                return value.ToString();

            if (character is not '\\')
            {
                value.Append(character);
                continue;
            }

            if (IsAtEnd)
                throw Error($"Unterminated escape sequence {context}.");

            value.Append(text[current++] switch
            {
                '"' => '"',
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw Error($"Unsupported escape sequence {context}.")
            });
        }
    }

    private string ParseIdentifier(string context)
    {
        if (!CanStartIdentifier(CurrentChar))
            throw Error($"Expected an identifier {context}.");

        int start = current++;

        while (!IsAtEnd && (char.IsLetterOrDigit(CurrentChar) || CurrentChar is '_'))
            current++;

        return text[start..current];
    }

    private void Expect(char expected, string context)
    {
        if (CurrentChar != expected)
            throw Error($"Expected '{expected}' {context}.");

        current++;
    }

    private void SkipWhitespace()
    {
        while (!IsAtEnd && char.IsWhiteSpace(CurrentChar))
            current++;
    }

    private bool IsAtEnd => current >= text.Length;
    private char CurrentChar => IsAtEnd ? '\0' : text[current];

    private static bool CanStartIdentifier(char character) => char.IsLetter(character) || character is '_';

    private MahoProjectParseException Error(string message) => new(message, current);
}

public sealed class MahoProjectParseException : Exception
{
    public int Position { get; }

    public MahoProjectParseException(string message, int position) : base($"{message} (at character {position}).") => Position = position;
}
