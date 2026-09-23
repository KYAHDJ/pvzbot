using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PvZController;

// Click-through desktop subtitle placed over the top of the PvZ window.
internal sealed class SubtitleWindow : Window
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x20;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x80;
    private readonly TextBlock _caption;
    private readonly DispatcherTimer _timer;
    private nint _gameWindow;
    private DateTime _visibleUntil;

    internal SubtitleWindow()
    {
        Width = 680;
        Height = 76;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;

        _caption = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 8, 16, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1, Opacity = 1
            }
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 12, 18, 32)),
            CornerRadius = new CornerRadius(12),
            Child = _caption
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle);
            SetWindowLongPtr(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdatePosition();
        Closed += (_, _) => _timer.Stop();
    }

    internal void ShowLine(string line, nint gameWindow)
    {
        _gameWindow = gameWindow;
        _caption.Text = line;
        _visibleUntil = DateTime.UtcNow.AddSeconds(Math.Clamp(line.Length / 15.0, 4.5, 9));
        UpdatePosition();
        _timer.Start();
    }

    private void UpdatePosition()
    {
        if (DateTime.UtcNow >= _visibleUntil || _gameWindow == 0 || IsIconic(_gameWindow) ||
            !GetWindowRect(_gameWindow, out var bounds))
        {
            Hide();
            _timer.Stop();
            return;
        }

        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var activePid);
        GetWindowThreadProcessId(_gameWindow, out var gamePid);
        if (activePid != gamePid) { Hide(); return; }

        var dpi = GetDpiForWindow(_gameWindow) / 96.0;
        if (dpi <= 0) dpi = 1;
        Width = Math.Min(680, Math.Max(320, (bounds.Right - bounds.Left) / dpi - 30));
        Left = bounds.Left / dpi + ((bounds.Right - bounds.Left) / dpi - Width) / 2;
        Top = bounds.Top / dpi + 112 / dpi;
        if (!IsVisible) Show();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
