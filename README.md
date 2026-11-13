# VSWatcher

A small WPF (.NET 10) desktop app that watches running Visual Studio instances on Windows. It discovers instances through the COM Running Object Table and reports each one's process ID, name and running state.

## Requirements

- Windows
- .NET 10 SDK (to build)

## Build and run

```powershell
dotnet build
dotnet run
```

## Installer

`installer.iss` is an [Inno Setup](https://jrsoftware.org/isinfo.php) script that packages a published build into `VSWatcherSetup.exe`.

## License

MIT - see [LICENSE](LICENSE).
