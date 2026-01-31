## OpenDownloader

A modern, open-source download manager built with Avalonia UI and .NET 8, powered by aria2.

### Features

- 🚀 **High Performance**: Powered by the robust `aria2` engine.
- 🎨 **Modern UI**: Clean, beautiful interface using Avalonia UI.
- 🖥️ **Cross-Platform**: Supports macOS, Windows, and Linux.
- 🌐 **Proxy Support**: HTTP and SOCKS5 proxy support.
- 🌗 **Theme Support**: Light and Dark modes.
- 🌍 **Bilingual**: English and Chinese (Simplified) support.

### Development

**Prerequisites:**
- .NET 8.0 SDK
- Avalonia templates

**Build:**
```bash
dotnet build src/OpenDownloader/OpenDownloader.csproj
```

**Run:**
```bash
dotnet run --project src/OpenDownloader/OpenDownloader.csproj
```

### Building for macOS

Use the provided script to package for macOS (creates a .dmg):

```bash
chmod +x build/package_osx.sh
./build/package_osx.sh osx-x64 1.0.0 build_output/
```

### License

MIT
