using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace MonitorInputWizzard;

/// <summary>One switchable input: a display name, the VCP 0x60 code, and an optional global hotkey.</summary>
public sealed class InputPreset : INotifyPropertyChanged
{
    private string _name = "Input";
    private uint _code;
    private ModifierKeys _modifiers;
    private Key _key;

    public string Name { get => _name; set => Set(ref _name, value); }
    public uint Code { get => _code; set { if (Set(ref _code, value)) OnChanged(nameof(CodeHex)); } }
    public ModifierKeys Modifiers { get => _modifiers; set { if (Set(ref _modifiers, value)) OnChanged(nameof(HotkeyText)); } }
    public Key Key { get => _key; set { if (Set(ref _key, value)) { OnChanged(nameof(HotkeyText)); OnChanged(nameof(HasHotkey)); } } }

    /// <summary>Code as an editable hex string ("0x11"). Bound by the UI instead of the raw number.</summary>
    [JsonIgnore]
    public string CodeHex
    {
        get => "0x" + _code.ToString("X2");
        set { if (TryParseHex(value, out uint c)) Code = c; }
    }

    [JsonIgnore] public bool HasHotkey => _key != Key.None;

    [JsonIgnore]
    public string HotkeyText => HasHotkey
        ? (_modifiers == ModifierKeys.None ? "" : _modifiers.ToString().Replace(", ", "+") + "+") + _key
        : "(none)";

    private static bool TryParseHex(string s, out uint value)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (Equals(field, value)) return false;
        field = value; OnChanged(name); return true;
    }
}

/// <summary>Presets + chosen monitor, persisted as JSON in %AppData%.</summary>
public sealed class Settings
{
    public string? MonitorDescription { get; set; }
    /// <summary>VCP register carrying input source. 0x60 = MCCS standard; LG UltraGear uses 0xF4.</summary>
    public byte InputRegister { get; set; } = 0x60;
    /// <summary>Switch via NVAPI raw I2C instead of dxva2 (needed for LG's 0x50 sidechannel).</summary>
    public bool UseNvApi { get; set; }
    /// <summary>DDC/CI source address byte. 0x51 = standard; 0x50 = LG service sidechannel.</summary>
    public byte SourceAddr { get; set; } = 0x50;
    public List<InputPreset> Inputs { get; set; } = new();

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MonitorInputWizzard", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? Defaults();
        }
        catch { /* corrupt/unreadable — fall back to defaults */ }
        return Defaults();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Defaults tuned for LG UltraGear (confirmed on 27GN800): input select lives on VCP 0xF4 over the
    // NVAPI 0x50 sidechannel, not standard 0x60. For a standard monitor: uncheck NVIDIA I2C, set Input
    // VCP 0x60, and use codes 0x0F/0x11/0x12.
    private static Settings Defaults() => new()
    {
        InputRegister = 0xF4,
        UseNvApi = true,
        SourceAddr = 0x50,
        Inputs = new()
        {
            new() { Name = "DisplayPort 1", Code = 0xD0 },
            new() { Name = "DisplayPort 2", Code = 0xD1 },
            new() { Name = "HDMI 1", Code = 0x90 },
            new() { Name = "HDMI 2", Code = 0x91 },
            new() { Name = "USB-C", Code = 0xD2 },
        }
    };
}
