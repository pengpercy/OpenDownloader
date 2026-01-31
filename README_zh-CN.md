## OpenDownloader

基于 Avalonia UI 和 .NET 10 构建的现代化开源下载管理器，由 aria2 强力驱动。

[English](README.md) | [简体中文](README_zh-CN.md)

### ✨ 特性

- 🚀 **高性能**：基于强大的 `aria2` 下载引擎。
- 🎨 **现代化 UI**：使用 Avalonia UI 构建的清爽美观界面。
- 🖥️ **跨平台**：完美支持 macOS、Windows 和 Linux。
- 🌐 **代理支持**：支持 HTTP 和 SOCKS5 代理设置。
- 🌗 **深色模式**：支持亮色和深色主题切换。
- 🌍 **双语支持**：内置英文和简体中文支持。

### 🛠️ 开发指南

**环境要求：**
- .NET 10.0 SDK
- Avalonia 模板

**构建项目：**
```bash
dotnet build src/OpenDownloader/OpenDownloader.csproj
```

**运行项目：**
```bash
dotnet run --project src/OpenDownloader/OpenDownloader.csproj
```

### 📦 macOS 打包

使用提供的脚本打包 macOS 应用（生成 .dmg 文件）：

```bash
chmod +x build/package_osx.sh
./build/package_osx.sh osx-x64 1.0.0 build_output/
```

### 📄 许可证

MIT
