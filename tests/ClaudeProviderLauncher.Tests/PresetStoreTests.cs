using System.IO;
using System.Text.Json;
using ClaudeProviderLauncher.Services;
using Xunit;

namespace ClaudeProviderLauncher.Tests;

public sealed class PresetStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ClaudeProviderLauncher.Tests", Guid.NewGuid().ToString("N"));

    private readonly string _filePath;

    public PresetStoreTests()
    {
        _filePath = Path.Combine(_directory, "presets.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }

    /// <summary>A store over a throwaway file with no built-in defaults, so tests see only what they write.</summary>
    private PresetStore CreateStore() => new(_filePath, loadDefaults: false);

    private static ProviderPreset CreatePreset(
        string name = "AnyModel",
        string baseUrl = "https://anymodel.org",
        string apiKey = "sk-secret-123",
        string model = "claude-sonnet-4-5",
        ToolKind tool = ToolKind.ClaudeCode)
        => new() { Name = name, BaseUrl = baseUrl, ApiKey = apiKey, Model = model, Tool = tool };

    // --- Loading -----------------------------------------------------------

    [Fact]
    public void Load_WhenFileIsMissing_ReturnsEmptyList()
    {
        var presets = CreateStore().Load();

        Assert.Empty(presets);
    }

    [Fact]
    public void Load_WhenFileIsCorrupted_ReturnsEmptyList()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, "{ not valid json !!!");

        Assert.Empty(CreateStore().Load());
    }

    [Fact]
    public void Load_WhenFileContainsForeignJson_ReturnsEmptyList()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, """{"somethingElse": true}""");

        Assert.Empty(CreateStore().Load());
    }

    [Fact]
    public void Load_SkipsEntriesWithBlankName()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, """
            {"Presets":[
              {"Name":"   ","BaseUrl":"https://blank","Model":"m","ApiKeyProtected":"","Tool":"ClaudeCode"},
              {"Name":"Real","BaseUrl":"https://real","Model":"m","ApiKeyProtected":"","Tool":"ClaudeCode"}
            ]}
            """);

        var loaded = Assert.Single(CreateStore().Load());

        Assert.Equal("Real", loaded.Name);
    }

    // --- Saving and loading (roundtrip) ------------------------------------

    [Fact]
    public void SaveThenLoad_RoundTripsAllPresetValues()
    {
        var store = CreateStore();
        var original = CreatePreset();

        Assert.True(store.Upsert(original));

        var loaded = Assert.Single(store.Load());
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.BaseUrl, loaded.BaseUrl);
        Assert.Equal(original.ApiKey, loaded.ApiKey);
        Assert.Equal(original.Model, loaded.Model);
        Assert.Equal(original.Tool, loaded.Tool);
    }

    [Fact]
    public void SaveThenLoad_PreservesMultiplePresetsInOrder()
    {
        var store = CreateStore();
        Assert.True(store.Upsert(CreatePreset(name: "First")));
        Assert.True(store.Upsert(CreatePreset(name: "Second")));
        Assert.True(store.Upsert(CreatePreset(name: "Third")));

        var names = store.Load().Select(p => p.Name).ToList();

        Assert.Equal(new[] { "First", "Second", "Third" }, names);
    }

    [Fact]
    public void Save_CreatesFileInTheGivenDirectory()
    {
        Assert.False(File.Exists(_filePath));

        CreateStore().Upsert(CreatePreset());

        Assert.True(Directory.Exists(_directory));
        Assert.True(File.Exists(_filePath));
        Assert.Equal(_filePath, CreateStore().UserFilePath);
    }

    [Fact]
    public void Save_CreatesMissingParentDirectoriesRecursively()
    {
        // The store must create the whole path, not assume the folder already exists.
        var nested = Path.Combine(_directory, "a", "b", "presets.json");
        var store = new PresetStore(nested, loadDefaults: false);

        Assert.True(store.Upsert(CreatePreset()));

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_PersistsToDisk_SurvivesNewStoreInstance()
    {
        CreateStore().Upsert(CreatePreset());

        // A fresh store instance (as after an app restart) reads the same file.
        var reloaded = CreateStore().Load();

        Assert.Single(reloaded);
    }

    [Fact]
    public void Save_WithEmptyApiKey_RoundTripsWithoutError()
    {
        var store = CreateStore();

        Assert.True(store.Upsert(CreatePreset(apiKey: string.Empty)));

        var loaded = Assert.Single(store.Load());
        Assert.Equal(string.Empty, loaded.ApiKey);
    }

    [Fact]
    public void Save_DropsDefaultPresets_FromTheUserFile()
    {
        var store = CreateStore();

        // Defaults live in the embedded resource; passing them to Save must not persist them.
        var saved = store.Save(new[]
        {
            CreatePreset(name: "Builtin"),
            new ProviderPreset { Name = "Default", BaseUrl = "https://d", Model = "m", ApiKey = "k", IsDefault = true },
        });

        Assert.True(saved);

        var loaded = store.Load();
        var only = Assert.Single(loaded);
        Assert.Equal("Builtin", only.Name);
        Assert.False(only.IsDefault);
    }

    // --- Security ----------------------------------------------------------

    [Fact]
    public void Save_DoesNotWriteApiKeyInPlainText()
    {
        const string secret = "sk-very-secret-token-42";

        CreateStore().Upsert(CreatePreset(apiKey: secret));

        var rawJson = File.ReadAllText(_filePath);

        Assert.DoesNotContain(secret, rawJson);
        Assert.Contains("ApiKeyProtected", rawJson);
    }

    [Fact]
    public void Load_WhenProtectedValueIsUnreadable_ReturnsEmptyKeyInsteadOfThrowing()
    {
        Directory.CreateDirectory(_directory);
        // Not valid DPAPI output: decoding/unprotecting must degrade to an empty key.
        File.WriteAllText(_filePath, """
            {"Presets":[{"Name":"AnyModel","BaseUrl":"https://a","Model":"m","ApiKeyProtected":"bm90LWRwYXBp","Tool":"ClaudeCode"}]}
            """);

        var loaded = Assert.Single(CreateStore().Load());

        Assert.Equal(string.Empty, loaded.ApiKey);
    }

    [Fact]
    public void Save_WhenFileCannotBeWritten_ReturnsFalseInsteadOfThrowing()
    {
        // An empty directory name makes the path impossible to create.
        var store = new PresetStore(Path.Combine(Path.GetInvalidFileNameChars()[0].ToString(), "presets.json"), loadDefaults: false);

        Assert.False(store.Upsert(CreatePreset()));
    }

    // --- Upsert / overwrite ------------------------------------------------

    [Fact]
    public void Upsert_WithNewName_AppendsPreset()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "First"));

        Assert.True(store.Upsert(CreatePreset(name: "Second", apiKey: "key-2")));

        var presets = store.Load();
        Assert.Equal(2, presets.Count);
        Assert.Equal(new[] { "First", "Second" }, presets.Select(p => p.Name));
    }

    [Fact]
    public void Upsert_WithExistingName_ModifiesValuesWithoutDuplicating()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(baseUrl: "https://old.example", apiKey: "old-key", model: "old-model"));

        Assert.True(store.Upsert(CreatePreset(baseUrl: "https://new.example", apiKey: "new-key", model: "new-model")));

        var loaded = Assert.Single(store.Load());
        Assert.Equal("https://new.example", loaded.BaseUrl);
        Assert.Equal("new-key", loaded.ApiKey);
        Assert.Equal("new-model", loaded.Model);
    }

    [Fact]
    public void Upsert_NameMatchIsCaseInsensitive_AndTrimsWhitespace()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "AnyModel"));

        Assert.True(store.Upsert(CreatePreset(name: "  anymodel  ", apiKey: "updated-key")));

        var loaded = Assert.Single(store.Load());
        Assert.Equal("anymodel", loaded.Name);
        Assert.Equal("updated-key", loaded.ApiKey);
    }

    [Fact]
    public void Upsert_WithBlankName_ReturnsFalseAndStoresNothing()
    {
        var store = CreateStore();

        Assert.False(store.Upsert(CreatePreset(name: "   ")));
        Assert.Empty(store.Load());
        Assert.False(File.Exists(_filePath));
    }

    [Fact]
    public void Upsert_ClearsIsDefaultFlag_OnTheStoredCopy()
    {
        var store = CreateStore();
        var preset = CreatePreset(name: "Sneaky", model: "m");
        preset.IsDefault = true;

        Assert.True(store.Upsert(preset));

        // Whatever the caller claimed, nothing written through Upsert comes back as a default.
        var loaded = Assert.Single(store.Load());
        Assert.False(loaded.IsDefault);
    }

    // --- Delete ------------------------------------------------------------

    [Fact]
    public void Delete_ExistingName_RemovesOnlyThatPreset()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "First"));
        store.Upsert(CreatePreset(name: "Second"));

        Assert.True(store.Delete(ToolKind.ClaudeCode, "First"));

        var remaining = Assert.Single(store.Load());
        Assert.Equal("Second", remaining.Name);
    }

    [Fact]
    public void Delete_MissingName_ReturnsFalseAndKeepsList()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "First"));

        Assert.False(store.Delete(ToolKind.ClaudeCode, "Nope"));
        Assert.Single(store.Load());
    }

    [Fact]
    public void Delete_IsCaseInsensitive()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "AnyModel"));

        Assert.True(store.Delete(ToolKind.ClaudeCode, "anymodel"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Delete_LastPreset_LeavesAnEmptyReadableFile()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset());

        Assert.True(store.Delete(ToolKind.ClaudeCode, "AnyModel"));

        Assert.Empty(store.Load());
        using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
        Assert.True(document.RootElement.TryGetProperty("Presets", out var presets));
        Assert.Equal(JsonValueKind.Array, presets.ValueKind);
        Assert.Equal(0, presets.GetArrayLength());
    }

    // --- Find (selection) --------------------------------------------------

    [Fact]
    public void Find_ExistingName_ReturnsPresetWithDecryptedKey()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "AnyModel", apiKey: "sk-selected"));

        var found = store.Find(ToolKind.ClaudeCode, "ANYMODEL");

        Assert.NotNull(found);
        Assert.Equal("sk-selected", found!.ApiKey);
        Assert.Equal("https://anymodel.org", found.BaseUrl);
        Assert.Equal("claude-sonnet-4-5", found.Model);
    }

    [Fact]
    public void Find_MissingName_ReturnsNull()
    {
        Assert.Null(CreateStore().Find(ToolKind.ClaudeCode, "Nope"));
    }

    [Fact]
    public void Find_WrongTool_ReturnsNull()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "AnyModel", tool: ToolKind.ClaudeCode));

        Assert.Null(store.Find(ToolKind.Codex, "AnyModel"));
    }

    [Fact]
    public void FindUser_IgnoresDefaultPresets()
    {
        // Defaults are read-only, so a user lookup must never hand one back.
        var store = new PresetStore(_filePath, loadDefaults: true);

        Assert.Null(store.FindUser(ToolKind.ClaudeCode, "AnyModel"));
    }

    // --- Built-in default protection ---------------------------------------

    [Fact]
    public void DefaultConstructor_LoadsBuiltInPresetsReadOnly()
    {
        var store = new PresetStore(_filePath, loadDefaults: true);

        var claudeDefaults = store.LoadDefaults(ToolKind.ClaudeCode);

        Assert.NotEmpty(claudeDefaults);
        Assert.All(claudeDefaults, p => Assert.True(p.IsDefault));
        Assert.Contains(claudeDefaults, p => p.Name == "AnyModel");
    }

    [Fact]
    public void Upsert_NameOfADefault_ReturnsFalseAndWritesNothing()
    {
        var store = new PresetStore(_filePath, loadDefaults: true);

        Assert.False(store.Upsert(CreatePreset(name: "AnyModel", baseUrl: "https://evil.example", apiKey: "stolen")));

        Assert.False(File.Exists(_filePath));
        Assert.Equal("https://anymodel.org", store.Find(ToolKind.ClaudeCode, "AnyModel")!.BaseUrl);
    }

    [Fact]
    public void Delete_DefaultName_ReturnsFalseAndKeepsThePreset()
    {
        var store = new PresetStore(_filePath, loadDefaults: true);

        Assert.False(store.Delete(ToolKind.ClaudeCode, "OpenRouter"));

        Assert.NotNull(store.Find(ToolKind.ClaudeCode, "OpenRouter"));
    }

    [Fact]
    public void IsNameReservedByDefault_TracksTheLoadedDefaultsState()
    {
        Assert.True(new PresetStore(_filePath, loadDefaults: true).IsNameReservedByDefault(ToolKind.ClaudeCode, "AnyModel"));
        Assert.False(new PresetStore(_filePath, loadDefaults: false).IsNameReservedByDefault(ToolKind.ClaudeCode, "AnyModel"));
    }

    [Fact]
    public void UserPreset_WithADefaultName_InAnotherTool_IsAllowed()
    {
        // Reserved names are scoped per tool, so Codex may use a Claude default's name.
        var store = new PresetStore(_filePath, loadDefaults: true);

        Assert.True(store.Upsert(CreatePreset(name: "AnyModel", tool: ToolKind.Codex, apiKey: "codex-key")));

        Assert.NotNull(store.Find(ToolKind.Codex, "AnyModel"));
        Assert.Equal("https://anymodel.org", store.Find(ToolKind.ClaudeCode, "AnyModel")!.BaseUrl);
    }

    [Fact]
    public void DeletingAUserPreset_LeavesDefaultsIntact()
    {
        var store = new PresetStore(_filePath, loadDefaults: true);
        store.Upsert(CreatePreset(name: "MyOwn", apiKey: "own-key"));

        Assert.True(store.Delete(ToolKind.ClaudeCode, "MyOwn"));

        var all = store.Load(ToolKind.ClaudeCode);
        Assert.All(all, p => Assert.True(p.IsDefault));
        Assert.Contains(all, p => p.Name == "AnyModel");
        Assert.Contains(all, p => p.Name == "OpenRouter");
    }

    // --- Storage format ----------------------------------------------------

    [Fact]
    public void SavedFile_IsReadableJsonDocument()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset());

        using var document = JsonDocument.Parse(File.ReadAllText(store.UserFilePath));

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("Presets", out var presets));
        Assert.Equal(JsonValueKind.Array, presets.ValueKind);
    }

    [Fact]
    public void SavedFile_WritesToolAsAString()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "CodexOne", tool: ToolKind.Codex));

        using var document = JsonDocument.Parse(File.ReadAllText(_filePath));
        var entry = document.RootElement.GetProperty("Presets")[0];

        Assert.Equal("Codex", entry.GetProperty("Tool").GetString());
    }

    [Fact]
    public void Load_WhenToolFieldIsMissing_FallsBackToClaudeCode()
    {
        // Older preset files predate the Tool field; they must keep working.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, """
            {"Presets":[{"Name":"Legacy","BaseUrl":"https://legacy","Model":"m","ApiKeyProtected":""}]}
            """);

        var loaded = Assert.Single(CreateStore().Load());

        Assert.Equal(ToolKind.ClaudeCode, loaded.Tool);
    }

    // --- Model contract ----------------------------------------------------

    [Fact]
    public void ProviderPreset_HasNoWorkingDirectoryProperty()
    {
        // Requirement: the working directory must never be part of a preset.
        Assert.Null(typeof(ProviderPreset).GetProperty("WorkingDirectory"));
    }

    [Fact]
    public void DefaultStore_WritesPresetsIntoAppPathsDataDirectory()
    {
        // The app is portable: its state sits next to the executable, not under %LOCALAPPDATA%.
        var store = new PresetStore();

        Assert.Equal(AppPaths.DataDirectory, Path.GetDirectoryName(store.UserFilePath));
        Assert.Equal("presets.json", Path.GetFileName(store.UserFilePath));
    }
}