# MonitorInputWizzard

A small Windows tray-less WPF utility that switches your monitor's **input source** (HDMI 1, DisplayPort 2, USB-C, …) with a global hotkey — or from the command line — instead of digging through the monitor's on-screen menu with the joystick nub on the back.

It talks to the display over **DDC/CI**, the side-channel that rides along the video cable. Two transports are supported:

| Transport | How | Use it when |
|---|---|---|
| **dxva2** (Windows `SetVCPFeature`) | Standard MCCS write to VCP register `0x60` | Your monitor is well-behaved and follows the spec |
| **NVAPI raw I2C** | Writes the DDC/CI packet directly on the I2C bus of an NVIDIA GPU | Your monitor ignores standard writes (notably LG UltraGear — see below) |

## Why the NVAPI path exists

Most monitors accept a plain `SetVCPFeature(0x60, <input code>)` and switch. Some don't. LG UltraGear panels (confirmed on the **27GN800**) silently ignore VCP `0x60` entirely. Their input select lives on a **non-standard register `0xF4`**, reachable only through the **`0x50` service side-channel** — an I2C source address that the Windows dxva2 API will not emit.

So `NvApi.cs` builds the DDC/CI packet by hand (length byte, Set-VCP opcode, register, hi/lo value, XOR checksum) and pushes it through `NvAPI_I2CWrite` on every connected display of every NVIDIA GPU. The LG acts on `0xF4`; other monitors just ignore a VCP code they don't know.

This is why the app ships with LG-friendly defaults. If you have a normal monitor, un-tick the NVIDIA box and you're on the standard path.

## Requirements

- Windows 10/11 (x64)
- .NET 10 SDK to build (the published binary is self-contained — no runtime install needed)
- **DDC/CI must be enabled in the monitor's OSD.** Many monitors ship with it off, and nothing will work until you turn it on.
- NVIDIA GPU + driver, *only* if you use the NVAPI transport (`nvapi64.dll` comes with the driver)

## Build & run

```
git clone https://github.com/Lunat1q/MonitorInputWizzard.git
cd MonitorInputWizzard
dotnet run
```

Single-file release build:

```
dotnet publish -c Release
```

The `.exe` lands in `bin/Release/net10.0-windows/win-x64/publish/`.

## Using it

The window is a list of **input presets**. Each preset is a name, a VCP code, and an optional global hotkey.

1. **Pick your transport.** Leave **Use NVIDIA I2C** ticked for an LG UltraGear; un-tick it for a standard monitor (which enables the monitor picker below).
2. **Set the input register.** `0x60` is the MCCS standard. `0xF4` is LG UltraGear.
3. **Find your input codes.** Switch the monitor to an input by hand, then hit **Read** — it prints the register's current value, which is that input's code. Repeat per input, typing each value into the preset's *Code* box.
4. **Bind a hotkey.** Click a preset's hotkey button, press the combo. `Esc` clears it. Hotkeys are system-wide (registered via `RegisterHotKey`, with auto-repeat suppressed) and fire even when the window isn't focused.
5. **Save.** Settings persist to `%AppData%\MonitorInputWizzard\settings.json`.

**Test** dims the selected monitor's brightness for ~1 second and restores it. If the screen visibly dims, DDC/CI writes are reaching the panel — so any input-switch failure is a wrong register or a wrong code, not a broken connection. (dxva2 mode only.)

### Default presets

Tuned for LG UltraGear — register `0xF4`, NVAPI on, source address `0x50`:

| Input | Code |
|---|---|
| DisplayPort 1 | `0xD0` |
| DisplayPort 2 | `0xD1` |
| HDMI 1 | `0x90` |
| HDMI 2 | `0x91` |
| USB-C | `0xD2` |

For a standard monitor on register `0x60`, the usual MCCS codes are `0x0F` (DisplayPort 1), `0x10` (DisplayPort 2), `0x11` (HDMI 1), `0x12` (HDMI 2).

## Command line

Passing an input name switches to it and exits immediately — no window:

```
MonitorInputWizzard.exe "HDMI 1"
MonitorInputWizzard.exe HDMI1          # spaces and case are ignored
MonitorInputWizzard.exe --switch "USB-C"
```

It reuses the saved settings, so configure once in the GUI, then wire the `.exe` into a Stream Deck button, an AutoHotkey script, or a shortcut.

## Troubleshooting

**Nothing happens.** Check DDC/CI is on in the monitor's OSD. Then hit **Test** — if brightness doesn't move, DDC/CI isn't getting through at all (bad/adapter cable, DDC/CI off, or a KVM/dock in the path eating the channel).

**Test dims fine, but the input won't switch.** Wrong register or wrong code. Use **Read** on each input to harvest the real codes. If `0x60` reads back as unsupported, try `0xF4` with the NVAPI box ticked.

**"No DDC/CI monitors found."** Windows sees no monitor exposing a physical DDC/CI handle. Common with DisplayLink/USB display adapters and some laptop internal panels — those simply can't be driven this way.

**Hotkey says it's already in use.** Another app grabbed that combo first. Pick a different one.

## Layout

| File | What it does |
|---|---|
| [MonitorController.cs](MonitorController.cs) | Enumerates physical monitors, reads/writes VCP features via `dxva2.dll` |
| [NvApi.cs](NvApi.cs) | Hand-rolled DDC/CI packets over NVAPI raw I2C, for monitors dxva2 can't reach |
| [HotkeyManager.cs](HotkeyManager.cs) | System-wide hotkeys via `RegisterHotKey` + a WndProc hook |
| [Settings.cs](Settings.cs) | Presets and transport config, persisted as JSON in `%AppData%` |
| [MainWindow.xaml.cs](MainWindow.xaml.cs) | The UI: preset list, hotkey capture, Read/Test diagnostics |
| [App.xaml.cs](App.xaml.cs) | Startup; headless command-line switch path |

## Known limits

- The NVAPI path **broadcasts** the write to every connected display on every NVIDIA GPU rather than targeting one. Harmless in practice — monitors ignore VCP codes they don't implement — but if it ever bites, per-display targeting by EDID match is the fix.
- NVAPI transport is NVIDIA-only. AMD/Intel users on a monitor that needs the side-channel are out of luck for now.
