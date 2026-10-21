using System.IO;
using System.Text;

namespace ClaudeProviderLauncher.Services;

/// <summary>
/// A read-only view of the Codex user configuration. The manager never writes
/// config.toml: provider overrides are passed to the Codex process at launch.
/// </summary>
public sealed class CodexConfigSnapshot
{
    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    /// <summary>The base URL of the active model provider, if present.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>The env_key of the active model provider, if present.</summary>
    public string? ApiKeyEnv { get; init; }

    public string? Error { get; init; }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
}

/// <summary>
/// Reads the top-level values the manager needs (<c>model</c>, <c>model_provider</c>)
/// and the active provider's <c>base_url</c> and <c>env_key</c> for display in the UI.
/// The config.toml is never modified by this application.
/// </summary>
public static class CodexConfigReader
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        "config.toml");

    public static CodexConfigSnapshot Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                // Codex creates the file itself on first use; nothing to read is valid.
                return new CodexConfigSnapshot();
            }

            string? model = null;
            string? modelProvider = null;
            string? baseUrl = null;
            string? apiKeyEnv = null;
            string? expectedProviderTable = null;
            var inProviderTable = false;
            var inAnyProviderTable = false;
            var lineNumber = 0;

            foreach (var rawLine in File.ReadLines(path))
            {
                lineNumber++;
                var line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith('['))
                {
                    // Table header - check if it's the active provider's table or any provider table
                    inProviderTable = false;
                    inAnyProviderTable = false;
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        var tableName = line[1..^1];
                        if (tableName.StartsWith("model_providers.", StringComparison.OrdinalIgnoreCase))
                        {
                            inAnyProviderTable = true;
                            if (expectedProviderTable is not null && tableName == expectedProviderTable)
                            {
                                inProviderTable = true;
                            }
                        }
                    }
                    continue;
                }

                var equals = FindUnquotedEquals(line);
                if (equals < 0)
                {
                    continue;
                }

                var key = line[..equals].Trim();

                if (inProviderTable)
                {
                    // Inside the active provider's table
                    if (key is ("base_url" or "env_key"))
                    {
                        var rawValue = line[(equals + 1)..].Trim();
                        if (!TryParseTomlString(rawValue, out var value, out var error))
                        {
                            return new CodexConfigSnapshot
                            {
                                Error = $"config.toml, line {lineNumber}: {error}",
                            };
                        }

                        if (key == "base_url")
                        {
                            baseUrl = value;
                        }
                        else
                        {
                            apiKeyEnv = value;
                        }
                    }
                    continue;
                }

                // Skip top-level keys if we're inside any provider table (but not the active one)
                if (inAnyProviderTable)
                {
                    continue;
                }

                // Top-level keys
                if (key is not ("model" or "model_provider"))
                {
                    continue;
                }

                var rawValueTop = line[(equals + 1)..].Trim();
                if (!TryParseTomlString(rawValueTop, out var valueTop, out var errorTop))
                {
                    return new CodexConfigSnapshot
                    {
                        Error = $"config.toml, line {lineNumber}: {errorTop}",
                    };
                }

                if (key == "model")
                {
                    model = valueTop;
                }
                else
                {
                    modelProvider = valueTop;
                    // The active provider's table is [model_providers.<model_provider>]
                    expectedProviderTable = "model_providers." + modelProvider;
                }
            }

            return new CodexConfigSnapshot
            {
                Model = model,
                ModelProvider = modelProvider,
                BaseUrl = baseUrl,
                ApiKeyEnv = apiKeyEnv,
            };
        }
        catch (Exception ex)
        {
            return new CodexConfigSnapshot
            {
                Error = "Could not read Codex config.toml: " + ex.Message,
            };
        }
    }

    private static int FindUnquotedEquals(string line)
    {
        var inDouble = false;
        var inSingle = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\\' && inDouble)
            {
                i++;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '=' && !inDouble && !inSingle)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryParseTomlString(string raw, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (raw.Length == 0)
        {
            error = "the value is missing.";
            return false;
        }

        if (raw[0] == '\'')
        {
            var close = raw.IndexOf('\'', 1);
            if (close < 0)
            {
                error = "unterminated literal string.";
                return false;
            }

            var rest = raw[(close + 1)..].Trim();
            if (rest.Length > 0 && !rest.StartsWith('#'))
            {
                error = "unexpected characters after the value.";
                return false;
            }

            value = raw[1..close];
            return true;
        }

        if (raw[0] != '"')
        {
            error = "the value must be a quoted string.";
            return false;
        }

        var builder = new StringBuilder();
        for (var i = 1; i < raw.Length; i++)
        {
            var c = raw[i];

            if (c == '"')
            {
                var rest = raw[(i + 1)..].Trim();
                if (rest.Length > 0 && !rest.StartsWith('#'))
                {
                    error = "unexpected characters after the value.";
                    return false;
                }

                value = builder.ToString();
                return true;
            }

            if (c == '\\')
            {
                if (i + 1 >= raw.Length)
                {
                    error = "unterminated escape sequence.";
                    return false;
                }

                i++;
                builder.Append(raw[i] switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => raw[i],
                });
                continue;
            }

            builder.Append(c);
        }

        error = "unterminated string.";
        return false;
    }
}
