using System.Windows;
using System.Windows.Controls;

namespace ClaudeProviderLauncher;

/// <summary>
/// Modal confirmation dialog styled with the app's custom title bar
/// (<see cref="PresetDialogShell"/>). Replaces the system MessageBox for preset
/// actions: shows a message, Enter confirms, Escape cancels.
/// </summary>
public static class PresetConfirmDialog
{
    /// <summary>Returns true when the user pressed the confirm button.</summary>
    public static bool Ask(Window owner, string title, string message, string confirmText)
    {
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        };

        var confirmButton = new Button
        {
            Content = confirmText,
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

        Grid.SetRow(messageText, 0);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(confirmButton);
        buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 1);

        grid.Children.Add(messageText);
        grid.Children.Add(buttons);

        var dialog = PresetDialogShell.Create(owner, title, grid, width: 400);

        confirmButton.Click += (_, _) => dialog.DialogResult = true;

        dialog.Loaded += (_, _) => confirmButton.Focus();

        return dialog.ShowDialog() == true;
    }
}
