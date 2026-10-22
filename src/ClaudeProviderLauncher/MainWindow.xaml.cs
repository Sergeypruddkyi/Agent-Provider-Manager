using System.IO;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ClaudeProviderLauncher.Services;

namespace ClaudeProviderLauncher;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IProfileFieldTarget
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(108, 203, 108));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(255, 107, 107));
    private static readonly SolidColorBrush WindowBackgroundBrush = new(Color.FromRgb(30, 30, 30));

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly PresetStore _presetStore = new();
    private readonly ProviderConfigurationManager _providerConfiguration = new();
    private readonly ICliDetector _cliDetector = new DefaultCliDetector();

    private ToolKind? _selectedTool;
    private SystemProfile? _systemProfile;
    private string? _activeProfileId;
    private List<ProfileChoice> _allProfiles = new();
    private bool _loadingFields;
    private bool _loadingProfile;

    private CliDetectionResult? _claudeCliResult;
    private CliDetectionResult? _codexCliResult;

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            AllowsTransparency = false;
            Background = WindowBackgroundBrush;
        }

        BaseUrlBox.TextChanged += ProfileField_Changed;
        ModelBox.TextChanged += ProfileField_Changed;
        ApiKeyBox.PasswordChanged += ProfileField_Changed;
        ProfileNameBox.TextChanged += ProfileNameBox_TextChanged;

        // Detect CLI availability on startup
        DetectAndUpdateCliStatus();

        // Start directly on Claude Code instead of showing the intermediate tool-selection screen.
        SelectTool(ToolKind.ClaudeCode);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Detects both CLIs and updates the status display.
    /// </summary>
    private void DetectAndUpdateCliStatus()
    {
        if (_claudeCliResult is null)
        {
            _claudeCliResult = _cliDetector.DetectClaude();
        }
        if (_codexCliResult is null)
        {
            _codexCliResult = _cliDetector.DetectCodex();
        }

        UpdateCliStatusDisplay();
    }

    private void UpdateCliStatusDisplay()
    {
        // Show both statuses when no tool is selected, or always show both
        if (_claudeCliResult is { } claudeResult)
        {
            if (claudeResult.IsFound)
            {
                ClaudeCliStatusText.Text = "Claude Code CLI: обнаружен";
                ClaudeCliStatusText.Foreground = SuccessBrush;
            }
            else
            {
                var message = claudeResult.ErrorMessage ?? "не найден";
                ClaudeCliStatusText.Text = $"Claude Code CLI: {message}";
                ClaudeCliStatusText.Foreground = ErrorBrush;
            }
        }

        if (_codexCliResult is { } codexResult)
        {
            if (codexResult.IsFound)
            {
                CodexCliStatusText.Text = "Codex CLI: обнаружен";
                CodexCliStatusText.Foreground = SuccessBrush;
            }
            else
            {
                var message = codexResult.ErrorMessage ?? "не найден";
                CodexCliStatusText.Text = $"Codex CLI: {message}";
                CodexCliStatusText.Foreground = ErrorBrush;
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Clips the window content to the rounded outline so hover/focus rectangles of
    /// corner buttons cannot paint square corners outside the Windows 11 radius.
    /// </summary>
    private void RootBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border && border.ActualWidth > 0 && border.ActualHeight > 0)
        {
            border.Clip = new RectangleGeometry(
                new Rect(0, 0, border.ActualWidth, border.ActualHeight),
                radiusX: 8,
                radiusY: 8);
        }
    }

    /// <summary>
    /// Every application start begins here: no tool is selected, no profile is
    /// loaded, and no tool configuration has been read or written.
    /// </summary>
    private void ResetToToolSelection()
    {
        _selectedTool = null;
        _systemProfile = null;
        _activeProfileId = null;

        ClearProfileFields();
        ClearProfileList();
        SettingsPanel.Visibility = Visibility.Collapsed;
        ToolHintText.Visibility = Visibility.Visible;
        StartButton.IsEnabled = false;
        StartButton.Content = "START";
        RestoreDefaultsButton.Visibility = Visibility.Collapsed;
        WorkingDirBox.Text = string.Empty;
        ClearStatus();
        UpdateToolButtonStyles();
    }

    private void ClaudeToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(ToolKind.ClaudeCode);

    private void CodexToolButton_Click(object sender, RoutedEventArgs e) => SelectTool(ToolKind.Codex);

    /// <summary>
    /// Switches tools from a clean state. Values from the previous tool are removed
    /// before the new tool's own default profile and presets are loaded.
    /// Does NOT read the config itself: the single ordered load runs in
    /// <see cref="ReloadProfiles"/> (read system config → build system profile →
    /// select system profile → fill the fields), so startup cannot mix orderings.
    /// </summary>
    private void SelectTool(ToolKind tool)
    {
        _selectedTool = tool;
        _activeProfileId = null;

        ClearProfileFields();
        ClearProfileList();
        ClearStatus();
        ProfileDropdownPopup.IsOpen = false;

        SettingsPanel.Visibility = Visibility.Visible;
        ToolHintText.Visibility = Visibility.Collapsed;
        StartButton.IsEnabled = true;
        StartButton.Content = tool.StartButtonText();
        RestoreDefaultsButton.Visibility = Visibility.Visible;

        // The working directory is chosen per project, not per tool or provider.
        // Keep a value the user already entered; on first selection load the last
        // explicitly chosen directory. Never fall back to the app's install folder.
        if (string.IsNullOrWhiteSpace(WorkingDirBox.Text))
        {
            RestoreLastWorkingDirectory();
        }

        UpdateToolButtonStyles();
        UpdateCliStatusDisplay();
        ReloadProfiles(selectId: SystemProfileIds.For(tool));
    }

    private void RestoreLastWorkingDirectory()
    {
        var last = LastValuesStore.Load();
        WorkingDirBox.Text =
            !string.IsNullOrWhiteSpace(last.WorkingDirectory) && Directory.Exists(last.WorkingDirectory)
                ? last.WorkingDirectory
                : string.Empty;
    }

    private void ClearProfileFields()
    {
        _loadingFields = true;
        try
        {
            BaseUrlBox.Text = string.Empty;
            ApiKeyBox.Password = string.Empty;
            ModelBox.Text = string.Empty;
        }
        finally
        {
            _loadingFields = false;
        }
    }

    private void ClearProfileList()
    {
        _allProfiles = new List<ProfileChoice>();
        ProfileNameBox.Text = string.Empty;
        _activeProfileId = null;
        ProfileDropdownList.ItemsSource = null;
        UpdatePresetActions();
        UpdateProfileLabels();
    }

    /// <summary>
    /// The single ordered loading path for both startup and changing the selected
    /// profile. Executed strictly in this sequence, once per load:
    /// 1. READ the live CLI configuration of the selected tool
    ///    (<c>~/.claude/settings.json</c> or <c>~/.codex/config.toml</c>) —
    ///    never a cached snapshot, never last entered values;
    /// 2. CREATE the system profile from what was read and rebuild the picker
    ///    list (system profile + user presets of this tool only);
    /// 3. SELECT the requested profile (system profile on startup);
    /// 4. FILL the fields: <see cref="LoadProfile"/> →
    ///    <see cref="ProfileFieldBinder.Apply(ProfileChoice?, IProfileFieldTarget)"/>
    ///    → <see cref="MainWindow.SetFields"/>, clearing any field the selected
    ///    profile does not have.
    /// No caller loads profile values by any other route.
    /// </summary>
    private void ReloadProfiles(string? selectId)
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        // Always read the live system profile to get current configuration.
        // This ensures returning to the system profile shows up-to-date values.
        var currentSystemProfile = SystemProfiles.Get(tool);
        _systemProfile = currentSystemProfile;

        var profiles = new List<ProfileChoice>
        {
            ProfileChoice.FromSystem(currentSystemProfile),
        };

        // Add user presets only. Default presets (built-in) are not shown in the dropdown
        // to avoid cluttering the UI. They remain available internally if needed.
        profiles.AddRange(_presetStore.LoadUser(tool).Select(ProfileChoice.FromPreset));

        _allProfiles = profiles;
        ProfileDropdownList.ItemsSource = profiles;

        var choice = profiles.FirstOrDefault(p => string.Equals(p.Id, selectId, StringComparison.Ordinal))
            ?? profiles[0];

        _loadingProfile = true;
        try
        {
            ProfileNameBox.Text = choice.DisplayName;
            _activeProfileId = choice.Id;
        }
        finally
        {
            _loadingProfile = false;
        }

        LoadProfile(choice);
        UpdatePresetActions();
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingProfile || _loadingFields)
        {
            return;
        }

        // User is typing a new preset name - detach from any selected profile
        // so field changes don't try to update the previous profile.
        _activeProfileId = null;
        UpdatePresetActions();
        UpdateProfileLabels();
    }

    private void LoadProfile(ProfileChoice choice)
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        _loadingProfile = true;
        try
        {
            _activeProfileId = choice.Id;

            // Fill the fields from the selected profile; missing values clear the
            // corresponding field (nothing is left over from the previous profile).
            ProfileFieldBinder.Apply(choice, this);
        }
        finally
        {
            _loadingProfile = false;
        }

        UpdateFieldVisibility();
        UpdateProfileLabels();
        UpdatePresetActions();

        if (choice.IsSystem && _systemProfile?.Error is { } error)
        {
            Fail($"{tool.DisplayName()} configuration cannot be used: " + error);
        }
    }

    /// <summary>
    /// The UI side of the profile loading path (see <see cref="ProfileFieldBinder"/>):
    /// pushes the selected profile's values into the three provider fields. The
    /// <c>_loadingFields</c> guard keeps these programmatic assignments from being
    /// treated as user edits.
    /// </summary>
    public void SetFields(string baseUrl, string apiKey, string model)
    {
        _loadingFields = true;
        try
        {
            BaseUrlBox.Text = baseUrl ?? string.Empty;
            ApiKeyBox.Password = apiKey ?? string.Empty;
            ModelBox.Text = model ?? string.Empty;
        }
        finally
        {
            _loadingFields = false;
        }
    }

    private void ProfileField_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is null || _loadingFields || _loadingProfile)
        {
            return;
        }

        // Editing fields never writes back to a system profile. It detaches the
        // form from the selected profile so Start uses the edited values only.
        // The profile NAME is kept — only the link to the selected profile is removed.
        _activeProfileId = null;
        UpdateFieldVisibility();
        UpdateProfileLabels();
        UpdatePresetActions();
    }

    private void UpdateFieldVisibility()
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        // Both tools always show the provider fields.
        // For Codex default profile, fields are populated from config.toml.
        BaseUrlRow.Visibility = Visibility.Visible;
        ApiKeyRow.Visibility = Visibility.Visible;
    }

    private void UpdateProfileLabels()
    {
        if (_selectedTool is not { } tool)
        {
            ActiveProfileText.Text = string.Empty;
            ProfileHintText.Text = string.Empty;
            return;
        }

        var selected = _allProfiles.FirstOrDefault(p => string.Equals(p.Id, _activeProfileId, StringComparison.Ordinal));
        var isSystem = selected?.IsSystem == true;
        var isDefault = selected?.IsDefault == true;
        var hasTypedName = !string.IsNullOrWhiteSpace(ProfileNameBox.Text);
        var profileName = selected?.DisplayName
            ?? (hasTypedName ? ProfileNameBox.Text.Trim()
            : "Manual values (not saved)");

        ActiveProfileText.Text = $"{tool.DisplayName()} — {profileName}";

        ProfileHintText.Text = (tool, isSystem, isDefault, selected, hasTypedName) switch
        {
            // System profiles (isSystem = true)
            (ToolKind.ClaudeCode, true, _, _, _) =>
                "Default Claude Code profile. The API key is never stored; enter it for this launch.",
            (ToolKind.Codex, true, _, _, _) =>
                "Default Codex profile using ~/.codex/config.toml. The file is never modified by this app.",

            // Default presets (isDefault = true, isSystem = false)
            (_, false, true, _, _) =>
                "Built-in default preset. To customize, save as a new user preset with a different name.",

            // User presets
            (ToolKind.Codex, false, false, { } choice, _) when string.IsNullOrWhiteSpace(choice.Preset?.BaseUrl) =>
                "User preset with the current Codex configuration.",
            (ToolKind.Codex, false, false, { }, _) =>
                "User preset with a custom API provider.",
            (_, false, false, _, true) =>
                "New preset. Enter a name and provider values, then click Save Preset.",
            (_, false, false, _, _) =>
                "Manual values (not saved). Save Preset creates a separate preset for this tool.",

            _ => string.Empty,
        };
    }

    private void UpdatePresetActions()
    {
        var selected = _allProfiles.FirstOrDefault(p => string.Equals(p.Id, _activeProfileId, StringComparison.Ordinal));
        var isUserPreset = selected is { IsSystem: false, IsDefault: false };
        var isDefaultPreset = selected is { IsDefault: true };
        DeletePresetButton.IsEnabled = _selectedTool is not null && isUserPreset;

        // Save Preset is always enabled when there's a name (even if not selected from list)
        // It will save/update the preset with that name.
        // If the name matches system profile or default preset, it will be rejected in SavePresetButton_Click.
    }

    private void UpdateToolButtonStyles()
    {
        SetToolButtonStyle(ClaudeToolButton, _selectedTool == ToolKind.ClaudeCode);
        SetToolButtonStyle(CodexToolButton, _selectedTool == ToolKind.Codex);
    }

    private static void SetToolButtonStyle(Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }
        else
        {
            button.ClearValue(FrameworkElement.StyleProperty);
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a working folder (Working Directory)",
        };

        if (Directory.Exists(WorkingDirBox.Text))
        {
            dialog.InitialDirectory = WorkingDirBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            WorkingDirBox.Text = dialog.FolderName;
        }
    }

    private void ProfileDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        ProfileDropdownPopup.IsOpen = true;
    }

    private void ProfileDropdownPopup_Closed(object? sender, EventArgs e)
    {
        // Popup closed without selection - no action needed
    }

    private void ProfileDropdownItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is ProfileChoice choice)
        {
            ProfileDropdownPopup.IsOpen = false;

            // A selection always re-runs the same loading path as startup: the profile
            // list is rebuilt with the freshly read system profile, so the fields show
            // the values of the profile selected NOW, never a snapshot captured earlier.
            // A system parameter missing from the current config clears its field
            // instead of keeping a previously loaded value.
            ReloadProfiles(choice.Id);
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        var name = ProfileNameBox.Text.Trim();

        if (name.Length == 0)
        {
            Fail("Enter a preset name.");
            return;
        }

        if (SystemProfiles.IsReservedName(tool, name))
        {
            Fail("This name is reserved for the system profile. Choose another name.");
            return;
        }

        var existingUserPreset = _presetStore.FindUser(tool, name);
        var existingDefault = _presetStore.IsNameReservedByDefault(tool, name);

        if (existingDefault)
        {
            Fail("This name is reserved for a built-in default preset. Choose another name.");
            return;
        }

        if (existingUserPreset is not null)
        {
            var confirm = PresetConfirmDialog.Ask(
                this,
                "Overwrite Preset",
                $"Preset \"{name}\" already exists for {tool.DisplayName()}." + Environment.NewLine +
                "Overwrite it with the current values?",
                "Overwrite");

            if (!confirm)
            {
                return;
            }
        }

        SavePreset(tool, name);
    }

    private void SavePreset(ToolKind tool, string name)
    {
        var preset = new ProviderPreset
        {
            Tool = tool,
            Name = name,
            BaseUrl = BaseUrlBox.Text.Trim(),
            ApiKey = ApiKeyBox.Password,
            Model = ModelBox.Text.Trim(),
        };

        if (!_presetStore.Upsert(preset))
        {
            Fail($"Failed to save preset \"{name}\".");
            return;
        }

        ReloadProfiles(ProfileChoice.UserIdPrefix + name);

        // Keep the name in the text field
        ProfileNameBox.Text = name;

        StatusText.Foreground = SuccessBrush;
        StatusText.Text = $"Preset \"{name}\" saved for {tool.DisplayName()}.";
        StatusText.Visibility = Visibility.Visible;
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        ClearProfileFields();

        // Clear the profile name field but keep Working Directory
        ProfileNameBox.Text = string.Empty;
        _activeProfileId = null;
        ProfileDropdownPopup.IsOpen = false;

        UpdateFieldVisibility();
        UpdateProfileLabels();
        UpdatePresetActions();
        ClearStatus();
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        var selected = _allProfiles.FirstOrDefault(p => string.Equals(p.Id, _activeProfileId, StringComparison.Ordinal));
        if (selected is not { IsSystem: false, Preset: { } preset })
        {
            return;
        }

        // Cannot delete default presets
        if (selected.IsDefault)
        {
            Fail("Built-in default presets cannot be deleted.");
            return;
        }

        var name = preset.Name;
        var confirm = PresetConfirmDialog.Ask(
            this,
            "Delete Preset",
            $"Delete the {tool.DisplayName()} preset \"{name}\"?" + Environment.NewLine +
            "This action cannot be undone. The system default profile is not affected.",
            "Delete");

        if (!confirm)
        {
            return;
        }

        if (!_presetStore.Delete(tool, name))
        {
            Fail($"Failed to delete preset \"{name}\".");
            return;
        }

        ReloadProfiles(SystemProfileIds.For(tool));

        StatusText.Foreground = SuccessBrush;
        StatusText.Text = $"Preset \"{name}\" deleted.";
        StatusText.Visibility = Visibility.Visible;
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is not { } tool)
        {
            return;
        }

        if (tool == ToolKind.ClaudeCode)
        {
            RestoreClaudeDefaults();
        }
        else
        {
            RestoreCodexDefaults();
        }
    }

    private void RestoreClaudeDefaults()
    {
        try
        {
            _providerConfiguration.Restore();
        }
        catch (Exception ex)
        {
            // Fields keep their current values when the restore itself failed.
            Fail("Failed to restore the original settings: " + ex.Message);
            return;
        }

        // The profile fields are synchronized with the restored original values so a
        // later Start re-uses them instead of re-writing the temporary profile. The
        // stale preset selection is dropped for the same reason: a still-selected
        // temporary preset would silently win over these fields at Start. Re-selecting
        // a preset afterwards is an explicit user choice and takes over again.
        // The working directory is intentionally left alone.
        _activeProfileId = null;
        ProfileNameBox.Text = string.Empty;
        ReloadProfiles(SystemProfileIds.For(ToolKind.ClaudeCode));

        var message = "Original settings restored. A backup copy is saved.";
        if (_providerConfiguration.TryGetOriginalProfile(out var original, out _))
        {
            _loadingFields = true;
            try
            {
                BaseUrlBox.Text = original.BaseUrl;
                ApiKeyBox.Password = original.ApiKey;
                ModelBox.Text = original.Model;
            }
            finally
            {
                _loadingFields = false;
            }
        }
        else
        {
            message += " The original settings do not contain a complete provider profile — fields not changed.";
        }

        UpdateFieldVisibility();
        UpdateProfileLabels();
        UpdatePresetActions();

        StatusText.Foreground = SuccessBrush;
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void RestoreCodexDefaults()
    {
        // The permanent backup is created once, on the very first Codex restore. If
        // config.toml never existed, there is nothing to restore.
        if (!CodexConfigBackup.PermanentBackupExists)
        {
            Fail("No backup of the original Codex configuration was found.");
            return;
        }

        if (!CodexConfigBackup.RestoreFromPermanentBackup())
        {
            Fail("Failed to restore the original Codex configuration.");
            return;
        }

        // Re-read the restored config so all fields reflect reality again.
        var restored = SystemProfiles.Get(ToolKind.Codex);
        _systemProfile = restored;

        _loadingFields = true;
        try
        {
            BaseUrlBox.Text = restored.Preset.BaseUrl ?? string.Empty;
            ApiKeyBox.Password = restored.Preset.ApiKey ?? string.Empty;
            ModelBox.Text = restored.Preset.Model ?? string.Empty;
        }
        finally
        {
            _loadingFields = false;
        }

        // Return to the system default profile for this tool.
        _activeProfileId = SystemProfileIds.For(ToolKind.Codex);
        ReloadProfiles(SystemProfileIds.For(ToolKind.Codex));

        UpdateFieldVisibility();
        UpdateProfileLabels();
        UpdatePresetActions();

        StatusText.Foreground = SuccessBrush;
        StatusText.Text = "Original Codex configuration restored from backup.";
        StatusText.Visibility = Visibility.Visible;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTool is not { } tool)
        {
            Fail("Select Claude Code or Codex first.");
            return;
        }

        var workingDirectory = WorkingDirBox.Text.Trim();

        // The working directory is mandatory, must exist, and is never replaced by
        // the manager's install folder or a previous provider's path.
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            Fail("Specify the working directory (Working Directory).");
            return;
        }

        if (!Directory.Exists(workingDirectory))
        {
            Fail("Working directory not found:" + Environment.NewLine + workingDirectory);
            return;
        }

        if (tool == ToolKind.ClaudeCode)
        {
            StartClaude(workingDirectory);
        }
        else
        {
            StartCodex(workingDirectory);
        }
    }

    private void StartClaude(string workingDirectory)
    {
        var baseUrl = BaseUrlBox.Text.Trim();
        var apiKey = ApiKeyBox.Password;
        var model = ModelBox.Text.Trim();

        var profile = new ProviderPreset
        {
            Tool = ToolKind.ClaudeCode,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model,
        };

        if (!ProviderConfigurationManager.IsValidProfile(baseUrl, apiKey, model))
        {
            Fail("Enter a complete Claude Code provider profile (Base URL, token and model).");
            return;
        }

        if (HasControlCharacters(baseUrl) || HasControlCharacters(apiKey) || HasControlCharacters(model))
        {
            Fail("Provider values must not contain line breaks or control characters.");
            return;
        }

        var claudePath = ClaudeCliLocator.Find();
        if (claudePath is null)
        {
            Fail("Claude Code CLI not found. Install it and make sure the \"claude\" command is available in PATH.");
            return;
        }

        try
        {
            _providerConfiguration.Apply(profile);
            ClaudeLauncher.Launch(claudePath, workingDirectory, profile.BaseUrl, profile.ApiKey, profile.Model);
        }
        catch (Exception ex)
        {
            Fail("Failed to start Claude Code:" + Environment.NewLine + ex.Message);
            return;
        }

        LastValuesStore.SaveClaudeValues(profile.BaseUrl, profile.Model, workingDirectory);

        StatusText.Foreground = SuccessBrush;
        StatusText.Text = "Claude Code started in a separate window.";
        StatusText.Visibility = Visibility.Visible;
    }

    private void StartCodex(string workingDirectory)
    {
        // Since the app relies on config.toml being readable by Codex, an invalid
        // file blocks the launch instead of being silently replaced.
        if (_systemProfile?.Error is { } configError)
        {
            Fail("Codex configuration cannot be used: " + configError +
                 Environment.NewLine + "Fix ~/.codex/config.toml or use a custom provider profile after fixing it.");
            return;
        }

        var isCustomProvider = _activeProfileId is not null
            && !string.Equals(_activeProfileId, SystemProfileIds.CodexDefault, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(BaseUrlBox.Text);
        var baseUrl = BaseUrlBox.Text.Trim();
        var apiKey = ApiKeyBox.Password;
        var model = ModelBox.Text.Trim();

        if (isCustomProvider)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Fail("Enter the provider Base URL for the custom Codex provider.");
                return;
            }

            if (!CodexLauncher.IsValidBaseUrl(baseUrl))
            {
                Fail("The Codex Base URL must be an absolute http:// or https:// URL.");
                return;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                Fail("Enter the model name for the custom Codex provider.");
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(baseUrl) || !string.IsNullOrWhiteSpace(apiKey))
        {
            // Defensive: values from a custom provider must never be applied while
            // the user chose Codex's own configuration.
            Fail("Clear the custom provider values or select the default Codex profile.");
            return;
        }

        if (HasControlCharacters(baseUrl) || HasControlCharacters(apiKey) || HasControlCharacters(model))
        {
            Fail("Provider values must not contain line breaks or control characters.");
            return;
        }

        var codexPath = CodexCliLocator.Find();
        if (codexPath is null)
        {
            Fail("Codex CLI not found. Install OpenAI Codex and make sure the \"codex\" command is available in PATH.");
            return;
        }

        try
        {
            CodexLauncher.Launch(codexPath, new CodexLaunchOptions
            {
                WorkingDirectory = workingDirectory,
                BaseUrl = isCustomProvider ? baseUrl : null,
                ApiKey = isCustomProvider ? apiKey : null,
                Model = string.IsNullOrWhiteSpace(model) ? null : model,
            });
        }
        catch (Exception ex)
        {
            Fail("Failed to start Codex:" + Environment.NewLine + ex.Message);
            return;
        }

        // Only the shared working directory is remembered; Codex provider values
        // must never overwrite Claude Code's saved default values.
        LastValuesStore.SaveWorkingDirectory(workingDirectory);

        StatusText.Foreground = SuccessBrush;
        StatusText.Text = "Codex started in a separate window.";
        StatusText.Visibility = Visibility.Visible;
    }

    
    private static bool HasControlCharacters(string value) =>
        value.Any(c => c is '\r' or '\n' or '\0');

    private void ClearStatus()
    {
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void Fail(string message)
    {
        StatusText.Foreground = ErrorBrush;
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
