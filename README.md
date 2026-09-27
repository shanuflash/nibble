# Nibble

A tiny Windows tray app for the **Royal Kludge M3** mouse. It shows battery at a glance and replaces the RK web driver for settings.

- **Tray icon.** Pick one of five styles: a mouse that fills up, the number, a battery, tinted, or minimal. They're drawn with Segoe Fluent Icons, so they sit next to the system icons. Hover for the exact percentage.
- **Flyout.** Click the icon for a Liquid Glass panel with battery ring, connection, DPI, and last sync.
- **Settings window.** DPI stages (value, active stage, LED colour), polling rate (125 Hz–8 kHz), sensor mode, Motion Sync, ripple control, angle snapping, glass mode, lift-off distance, debounce, and sleep timer. Everything is read from and written to the mouse directly.
- **Firmware check.** Compares the receiver/mouse firmware with RK's published versions and links to RK's official updater. Nibble never flashes firmware itself.
- **Alerts.** Notifies you at 20% and when the mouse is fully charged.
- **Lightweight.** One ~130 KB exe with no dependencies (it uses the .NET Framework that ships with Windows). It sits at about 2 MB working set while idle and does no work between checks.

> Not affiliated with Royal Kludge. "RK" and "Royal Kludge" are their trademarks.

## Build

```
build.cmd
```

The output goes to `bin\Nibble.exe`. There's no installer; turn on **Launch at login** in Settings → General.

### Releases

Go to **Actions → Build → Run workflow**, enter a version, and tick **release** to publish it as a GitHub Release with the exe attached. The workflow only ever runs manually. Nibble's own update check (Settings → General) reads the latest release.

Dev flags:

- `--show` opens the flyout, and `--settings` opens the settings window.
- `--snapshot out.png dark|light [percent] [charging|asleep]` renders the flyout to a PNG.
- `--snapshot-settings out.png dark|light <page>` renders a settings page.

## How it talks to the M3

Everything goes through 64-byte HID feature reports (ID 3) on the receiver's vendor collection, usage page `0xFF00`. The receiver is VID `0x372E`, PID `0x1019`, or `0x102A` over the cable.

```
request  [03][crc][50][len][sn][cmd][offset][payload…]   crc = sum(bytes 2..62) & 0xFF
reply    [03][50][status][len][sn][offset][data…]        each reply can be read once
```

| cmd | what |
|---|---|
| `4F` read | Offset `0x81` is status (battery = `data[0] & 0x7F`, bit 7 = charging). Offsets `0x40`–`0x45` are the 56-byte settings table in 10-byte chunks. |
| `35` write | Config bytes at an offset: polling rate (1), LOD/debounce/perf bits (3), sleep (8). |
| `3A` write | One DPI stage: `[stage<<4 \| count, dpi_lo, dpi_hi, r, g, b]`. The DPI is stored as `dpi/50 − 1` up to 30000. |
| `06` | Factory reset. |

Firmware version uses the OTA frame family (`0x65 … 0x92`). Live events arrive on usage page `0xFF06` as input report 9: `09 FA <type> <value>`, where type 2 = link, 3 = DPI stage, and 5 = battery.

Everything above was worked out from drive.rkgaming.com's JavaScript and checked against a real M3.

## Roadmap

Nibble is going modular: a small driver interface plus a `devices/` folder, so adding a mouse means adding a device file rather than forking the app. The M3 will be the first device.

## Privacy

Nibble only talks to the mouse over USB. The one network request is RK's firmware manifest, and it's sent only when you open Settings → Device.
