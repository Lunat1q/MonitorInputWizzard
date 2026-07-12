using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MonitorInputWizzard;

public partial class MainWindow : Window
{
    private readonly MonitorController _monitors = new();
    private readonly Settings _settings = Settings.Load();
    private readonly ObservableCollection<InputPreset> _inputs;
    private HotkeyManager? _hotkeys;
    private NvApi? _nvApi;
    private InputPreset? _capturing;

    public MainWindow()
    {
        InitializeComponent();
        _inputs = new(_settings.Inputs);
        InputList.ItemsSource = _inputs;
        InputRegBox.Text = "0x" + _settings.InputRegister.ToString("X2");
        SrcBox.Text = "0x" + _settings.SourceAddr.ToString("X2");
        NvCheck.IsChecked = _settings.UseNvApi;
        MonitorRow.IsEnabled = !_settings.UseNvApi;
        PreviewKeyDown += OnCaptureKey;
        Loaded += (_, _) => { _hotkeys = new(this); RefreshMonitors(); RegisterHotkeys(); };
        Closed += (_, _) => { _hotkeys?.Dispose(); _nvApi?.Dispose(); _monitors.Dispose(); };
    }

    private static byte ParseHexByte(string s, byte fallback)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return byte.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var b) ? b : fallback;
    }

    private void RefreshMonitors()
    {
        var list = _monitors.Enumerate();
        MonitorBox.ItemsSource = list;
        MonitorBox.SelectedItem = list.FirstOrDefault(m => m.Description == _settings.MonitorDescription);
        if (MonitorBox.SelectedItem is null && list.Count > 0) MonitorBox.SelectedIndex = 0;
        Status.Text = list.Count == 0 ? "No DDC/CI monitors found." : $"{list.Count} monitor(s).";
    }

    private MonitorController.Monitor? Selected =>
        MonitorBox.SelectedItem is MonitorController.Monitor m ? m : null;

    private byte InputRegister => ParseHexByte(InputRegBox.Text, 0x60);

    private void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        _hotkeys.Dispose();
        _hotkeys = new(this);
        foreach (var input in _inputs.Where(i => i.HasHotkey))
        {
            var target = input;
            if (_hotkeys.Register(input.Modifiers, input.Key, () => SwitchTo(target)) is null)
                Status.Text = $"Hotkey for \"{input.Name}\" is already in use.";
        }
    }

    private void SwitchTo(InputPreset input)
    {
        byte reg = InputRegister;
        if (NvCheck.IsChecked == true)
        {
            try { _nvApi ??= new NvApi(); }
            catch (Exception ex) { Status.Text = "NVAPI unavailable: " + ex.Message; return; }
            string log = _nvApi.SetVcp(reg, (ushort)input.Code, ParseHexByte(SrcBox.Text, 0x50));
            Status.Text = $"NVAPI {input.Name} VCP 0x{reg:X2}=0x{input.Code:X2} src 0x{ParseHexByte(SrcBox.Text, 0x50):X2}: {log.Trim()}";
            return;
        }
        if (Selected is not { } m) { Status.Text = "No monitor selected."; return; }
        bool ok = _monitors.SetVcp(m.Handle, reg, input.Code);
        Status.Text = ok
            ? $"Sent {input.Name} (VCP 0x{reg:X2} = 0x{input.Code:X2}). If nothing changed, wrong register/code for this monitor."
            : $"SetVCPFeature failed for {input.Name} (VCP 0x{reg:X2} = 0x{input.Code:X2}).";
    }

    // ---- Hotkey capture ----

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = ((FrameworkElement)sender).DataContext as InputPreset;
        Status.Text = "Press a key combo (Esc to clear)...";
    }

    private void OnCaptureKey(object sender, KeyEventArgs e)
    {
        if (_capturing is null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return; // wait for the real key

        e.Handled = true;
        if (key == Key.Escape) { _capturing.Modifiers = ModifierKeys.None; _capturing.Key = Key.None; }
        else { _capturing.Modifiers = Keyboard.Modifiers; _capturing.Key = key; }
        _capturing = null;
        RegisterHotkeys();
    }

    // ---- Buttons ----

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is InputPreset p) SwitchTo(p);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
        => _inputs.Add(new() { Name = "New input", Code = 0x0F });

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is InputPreset p)
        {
            _inputs.Remove(p);
            RegisterHotkeys();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Inputs = _inputs.ToList();
        _settings.MonitorDescription = Selected?.Description;
        _settings.InputRegister = InputRegister;
        _settings.UseNvApi = NvCheck.IsChecked == true;
        _settings.SourceAddr = ParseHexByte(SrcBox.Text, 0x50);
        _settings.Save();
        RegisterHotkeys();
        Status.Text = "Saved.";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMonitors();

    // NVAPI broadcasts to all displays, so the dxva2 monitor picker/Read/Test don't apply in that mode.
    private void NvMode_Changed(object sender, RoutedEventArgs e) => MonitorRow.IsEnabled = NvCheck.IsChecked != true;

    // Dims brightness for ~1s then restores it, to prove DDC/CI writes actually reach the panel.
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } m) { Status.Text = "No monitor selected."; return; }
        if (_monitors.GetVcp(m.Handle, 0x10) is not { } b) { Status.Text = "Can't read brightness (0x10) — DDC/CI read failed."; return; }
        bool wrote = _monitors.SetVcp(m.Handle, 0x10, Math.Min(20, b.max / 4));
        Status.Text = wrote ? "Dimming... watch the screen." : "Brightness write returned failure.";
        await Task.Delay(1200);
        _monitors.SetVcp(m.Handle, 0x10, b.cur);
        Status.Text = wrote ? "Restored. If it dimmed, DDC writes work — the input issue is the code or auto-switch." : Status.Text;
    }

    private void Read_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } m) { Status.Text = "No monitor selected."; return; }
        byte reg = InputRegister;
        var code = _monitors.GetVcp(m.Handle, reg)?.cur;
        Status.Text = code is { } c
            ? $"VCP 0x{reg:X2} current = 0x{c:X2}  (set an input's code to this)"
            : $"Could not read VCP 0x{reg:X2} — unsupported, or DDC/CI disabled in the OSD.";
    }

    private void Monitor_Changed(object sender, SelectionChangedEventArgs e)
        => _settings.MonitorDescription = Selected?.Description;
}
