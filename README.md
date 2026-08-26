# Media Converter

Modern Windows app for drag-and-drop audio/video conversion.

This is a public open-source project released under the MIT License.

## Run

Download `MediaConverter.exe` from the GitHub Releases page, or open the locally published single-file executable:

`publish-single\MediaConverter.exe`

There is also a smaller framework-dependent build at `publish\MediaConverter.exe`, but it needs the adjacent `.dll` and `.json` files.

## Build

Requirements:

- Windows
- .NET 10 SDK
- `ffmpeg.exe` for conversion
- `ffprobe.exe` for duration display

Build:

```powershell
dotnet build .\MediaConverterApp.csproj -c Release
```

Publish a self-contained Windows executable:

```powershell
dotnet publish .\MediaConverterApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish-single
```

## Defaults

- Output format: `mp3`
- Output location: same folder as the input file
- Include subfolders when adding folders
- Preserve originals
- Skip existing output files
- Theme: `Abyss`
- Quality preset: `Balanced`
- Logs/settings: `%LOCALAPPDATA%\MediaConverter`

## Themes

Use the `Theme` dropdown in the top-right corner to switch between:

- `Abyss`
- `Ember`
- `Neon`
- `Royal`

The app executable uses `MediaConverter.ico` as its Windows icon.

## Conversion Options

- Quality presets: `Small file`, `Balanced`, `High quality`, `Custom`
- Naming rules: `Same name`, `Append _converted`, `Auto-number conflicts`, `VRC-safe filename`
- Optional single output folder
- Optional folder structure preservation when using one output folder
- Optional ffmpeg.exe path picker

## Queue Controls

- Cancel running conversion
- Pause after the current file finishes
- Clear converted items with the visible button
- Use the last-run summary strip to open the latest log, retry failed/skipped/cancelled items, or clear converted items
- Right-click queue rows to remove, retry, open folders, or clear converted items
- Duration loads in the background when `ffprobe.exe` is available next to ffmpeg or on PATH
- Use the `Help` button for an in-app explanation of presets, naming rules, logs, and delete-original safety

## Output Formats

`mp3`, `wav`, `ogg`, `flac`, `m4a`

## Safety

Original files are only deleted when `Delete originals after success` is checked. The app only deletes after ffmpeg succeeds and the output file exists with non-zero size.

## License

MIT License. See [LICENSE](LICENSE).
