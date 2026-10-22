namespace ClaudeProviderLauncher.Services;

/// <summary>
/// Receives the concrete values a selected profile puts into the three provider
/// fields (Base URL, API Key / Auth Token, Model). <see cref="MainWindow"/>
/// implements this by assigning the WPF controls; tests implement it with a
/// recorder, so the profile-to-fields step can be verified without the full UI.
/// </summary>
public interface IProfileFieldTarget
{
    /// <summary>Assigns the field values; every value becomes the field's text.</summary>
    void SetFields(string baseUrl, string apiKey, string model);
}

/// <summary>
/// The last step of the loading path "read the CLI config → build the system
/// profile → select the profile → fill the fields". It delivers the selected
/// profile's values to <see cref="IProfileFieldTarget"/>; a missing value is
/// delivered as an empty string so switching profiles can never leave stale
/// values from the previously selected profile or preset.
/// </summary>
public static class ProfileFieldBinder
{
    public static void Apply(ProfileChoice? choice, IProfileFieldTarget target)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        // Always pass the complete tuple: every field is assigned, so any field
        // that is absent in the new profile is cleared instead of keeping the
        // previous profile's value.
        target.SetFields(
            choice?.Preset?.BaseUrl ?? string.Empty,
            choice?.Preset?.ApiKey ?? string.Empty,
            choice?.Preset?.Model ?? string.Empty);
    }
}
