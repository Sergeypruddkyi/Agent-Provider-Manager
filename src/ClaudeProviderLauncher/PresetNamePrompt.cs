using System.Windows;
using System.Windows.Controls;

namespace ClaudeProviderLauncher;

/// <summary>
/// Modal dialog that asks for a preset name. Uses the app's custom title bar and
/// existing button styles via <see cref="PresetDialogShell"/>; Enter saves,
/// Escape (or the close button) cancels.
/// </summary>
public static class PresetNamePrompt
{
    /// <summary>Returns the trimmed name, or null when the user cancelled.</summary>
    public static string? Ask(Window owner, string initialName)
    {
        var nameBox = new TextBox
        {
            Text = initialName,
            Height = 30,
        };

        var saveButton = new Button
        {
            Content = "Save",
            MinWidth = 88,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 88,
            IsCancel = true,
        };

        var grid = new Grid { Margin = new Thickness(16, 12, 16, 16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Preset name:",
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(label, 0);

        Grid.SetRow(nameBox, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 2);

        grid.Children.Add(label);
        grid.Children.Add(nameBox);
        grid.Children.Add(buttons);

        var dialog = PresetDialogShell.Create(owner, "Save Preset", grid, width: 380);

        saveButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                dialog.DialogResult = true;
            }
        };

        dialog.Loaded += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? nameBox.Text.Trim() : null;
    }
}
