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

    private PresetStore CreateStore() => new(_filePath);

    private static ProviderPreset CreatePreset(
        string name = "AnyModel",
        string baseUrl = "https://anymodel.org",
        string apiKey = "sk-secret-123",
        string model = "claude-sonnet-4-5")
        => new() { Name = name, BaseUrl = baseUrl, ApiKey = apiKey, Model = model };

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
    public void Save_WhenFileCannotBeWritten_ReturnsFalseInsteadOfThrowing()
    {
        // A file path with an invalid character can never be created.
        var store = new PresetStore("\0invalid-path");

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
    public void Upsert_WithExistingName_OverwritesValuesWithoutDuplicating()
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
    }

    // --- Delete ------------------------------------------------------------

    [Fact]
    public void Delete_ExistingName_RemovesOnlyThatPreset()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "First"));
        store.Upsert(CreatePreset(name: "Second"));

        Assert.True(store.Delete("First"));

        var remaining = Assert.Single(store.Load());
        Assert.Equal("Second", remaining.Name);
    }

    [Fact]
    public void Delete_MissingName_ReturnsFalseAndKeepsList()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "First"));

        Assert.False(store.Delete("Nope"));
        Assert.Single(store.Load());
    }

    [Fact]
    public void Delete_IsCaseInsensitive()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "AnyModel"));

        Assert.True(store.Delete("anymodel"));
        Assert.Empty(store.Load());
    }

    // --- Find (selection) --------------------------------------------------

    [Fact]
    public void Find_ExistingName_ReturnsPresetWithDecryptedKey()
    {
        var store = CreateStore();
        store.Upsert(CreatePreset(name: "AnyModel", apiKey: "sk-selected"));

        var found = store.Find("ANYMODEL");

        Assert.NotNull(found);
        Assert.Equal("sk-selected", found!.ApiKey);
        Assert.Equal("https://anymodel.org", found.BaseUrl);
        Assert.Equal("claude-sonnet-4-5", found.Model);
    }

    [Fact]
    public void Find_MissingName_ReturnsNull()
    {
        Assert.Null(CreateStore().Find("Nope"));
    }

    // --- Model contract ----------------------------------------------------

    [Fact]
    public void ProviderPreset_HasNoWorkingDirectoryProperty()
    {
        // Requirement: the working directory must never be part of a preset.
        Assert.Null(typeof(ProviderPreset).GetProperty("WorkingDirectory"));
    }

    [Fact]
    public void DefaultStore_UsesPerUserDataDirectoryNextToOtherSettings()
    {
        var expectedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeProviderLauncher");

        var store = new PresetStore();

        Assert.Equal(expectedDirectory, Path.GetDirectoryName(store.FilePath));
        Assert.Equal("presets.json", Path.GetFileName(store.FilePath));
    }

    [Fact]
    public void SavedFile_IsReadableJsonDocument()
    {
        CreateStore().Upsert(CreatePreset());

        using var document = JsonDocument.Parse(File.ReadAllText(_filePath));

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("Presets", out var presets));
        Assert.Equal(JsonValueKind.Array, presets.ValueKind);
    }
}
