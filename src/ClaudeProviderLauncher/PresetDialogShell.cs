using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace ClaudeProviderLauncher;

/// <summary>
/// Builds the borderless dark shell used by the preset dialogs. It mirrors the
/// main window's custom title bar (icon, title, close button, drag) and reuses the
/// existing application resources from App.xaml - no new styles are introduced.
/// </summary>
internal static class PresetDialogShell
{
    /// <summary>
    /// Returns a ready-to-use modal dialog window: custom title bar on top,
    /// caller-provided content below, positioned over <paramref name="owner"/>.
    /// </summary>
    public static Window Create(Window owner, string title, UIElement content, double width = 380)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,

            // Transparent so the rounded corners of the border below are the real window
            // outline (a solid background would paint square corners).
            Background = Brushes.Transparent,
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/icon.png")),
        };

        // Same chrome as MainWindow: the title bar is drawn by us, not by Windows.
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 36,
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        var closeButton = new Button
        {
            Content = "\uE8BB",
            Style = (Style)Application.Current.Resources["TitleBarCloseButtonStyle"],
            ToolTip = "Close",
        };
        closeButton.Click += (_, _) => window.Close();

        var titleBar = new Grid { Background = Brush("BrushBack") };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                window.DragMove();
            }
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        var icon = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/icon.png")),
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        header.Children.Add(icon);

        // The global TextBlock style (Segoe UI 13, BrushTextLight) matches the main window title.
        header.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(header, 0);
        titleBar.Children.Add(header);

        Grid.SetColumn(closeButton, 1);
        titleBar.Children.Add(closeButton);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(content, 1);
        root.Children.Add(titleBar);
        root.Children.Add(content);

        // Windows 11 style rounded corners; SizeChanged clips children (title bar,
        // close-button hover) to the rounded outline so no square corners appear.
        var border = new Border
        {
            BorderBrush = Brush("BrushBorder"),
            BorderThickness = new Thickness(1),
            Background = Brush("BrushBack"),
            CornerRadius = new CornerRadius(8),
            Child = root,
        };
        border.SizeChanged += (_, _) =>
        {
            if (border.ActualWidth > 0 && border.ActualHeight > 0)
            {
                border.Clip = new RectangleGeometry(
                    new Rect(0, 0, border.ActualWidth, border.ActualHeight),
                    radiusX: 8,
                    radiusY: 8);
            }
        };

        window.Content = border;

        return window;
    }

    private static SolidColorBrush Brush(string key)
    {
        return (SolidColorBrush)Application.Current.Resources[key];
    }
}
