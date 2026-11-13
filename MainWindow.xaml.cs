using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VSWatcher;

public partial class MainWindow : Window
{
    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSWatcher", "settings.json");

    readonly VsService _service;
    IReadOnlyList<VsSnapshot> _last = [];

    public MainWindow()
    {
        InitializeComponent();
        LoadPosition();
        Render([]);
        _service = new VsService(s => Dispatcher.BeginInvoke(() => Render(s)));
        Closing += (_, _) => { SavePosition(); _service.Dispose(); };
    }

    void Drag(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Close_Click(object s, RoutedEventArgs e) => Close();

    void Render(IReadOnlyList<VsSnapshot> snaps)
    {
        if (_last.SequenceEqual(snaps) && Stack.Children.Count > 0) return;
        _last = snaps;
        Stack.Children.Clear();
        if (snaps.Count == 0)
        {
            Stack.Children.Add(new TextBlock { Text = "Ingen Visual Studio åben", Foreground = Brushes.Gray, Margin = new Thickness(6) });
            return;
        }
        foreach (var s in snaps) Stack.Children.Add(Row(s));
    }

    UIElement Row(VsSnapshot s)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
            Fill = s.Running ? Brushes.LimeGreen : Brushes.Gray
        };
        var name = new TextBlock
        {
            Text = s.Name, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 12, 0), Cursor = Cursors.Hand
        };
        name.MouseLeftButtonUp += (_, _) => _service.Activate(s.Pid);

        var main = Btn(s.Running ? "■" : "▶", s.Running ? "Stop" : "Start debugging (F5)",
            s.Running ? Brushes.IndianRed : Brushes.LimeGreen,
            () => { if (s.Running) _service.Stop(s.Pid); else _service.Play(s.Pid); });

        Grid.SetColumn(name, 1); Grid.SetColumn(main, 3);
        grid.Children.Add(dot); grid.Children.Add(name); grid.Children.Add(main);
        if (s.Running)
        {
            var restart = Btn("⟳", "Genstart", Brushes.Orange, () => _service.Restart(s.Pid));
            Grid.SetColumn(restart, 2);
            grid.Children.Add(restart);
        }
        return grid;
    }

    static Button Btn(string text, string tip, Brush fg, Action click)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Foreground = fg, Width = 28, Height = 24, FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)), BorderThickness = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0), Cursor = Cursors.Hand
        };
        b.Click += (_, _) => click();
        return b;
    }

    record Pos(double Left, double Top);

    void LoadPosition()
    {
        try
        {
            var p = JsonSerializer.Deserialize<Pos>(File.ReadAllText(SettingsPath));
            if (p == null) throw new InvalidOperationException();
            Left = Math.Clamp(p.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100);
            Top = Math.Clamp(p.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        catch { WindowStartupLocation = WindowStartupLocation.CenterScreen; }
    }

    void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Pos(Left, Top)));
        }
        catch { }
    }
}
