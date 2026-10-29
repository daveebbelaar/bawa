# Existing apps

The commands in `build-and-verify` assume the `new-windows-app` template.
For an app made another way, map each one before the first change.

## Find out what it is

```powershell
Get-ChildItem -Recurse -Include *.sln,*.slnx,*.csproj,*.vcxproj,Package.appxmanifest | Select-Object FullName
Select-String -Path **\*.csproj -Pattern 'UseWinUI|UseWPF|UseWindowsForms|WindowsPackageType|TargetFramework'
```

| You find | It is | Build with |
| --- | --- | --- |
| `UseWinUI` and `WindowsPackageType None` | unpackaged WinUI 3, like the template | `dotnet build -c Release`, run the exe from the bin folder |
| `UseWinUI` and a `Package.appxmanifest` | packaged WinUI 3 (MSIX) | `dotnet build -p:Platform=x64`; install with `Add-AppxPackage -Register <bin>\AppxManifest.xml` (Developer Mode on) |
| `UseWPF` | WPF | `dotnet build`, run the exe |
| `UseWindowsForms` | WinForms | `dotnet build`, run the exe |
| `.vcxproj` | C++ (Win32 or C++/WinRT) | `msbuild`, which needs Visual Studio Build Tools |
| `net4x` target | .NET Framework | `msbuild` from Build Tools; plan a move to .NET 10 |

## Add the agent modes

Snapshot, self-test, and seed modes are what let an agent check its own
work. Port them from the template in this order, one commit each:

1. `AgentLog.cs` and the `APP_STORE` override in the app's data path.
2. `SelfTest.cs`, called once the main window has loaded.
3. `Snapshot.cs` with its `Win32.cs` calls. For WPF, capture with
   `RenderTargetBitmap` on the window content instead of `PrintWindow`;
   it misses the title bar, which is fine for content checks.
4. `tools/common.ps1`, `snap.ps1`, and `selftest.ps1`, with the exe path
   pointed at the app's bin folder.

## Packaged apps

A packaged app reads its settings from `ApplicationData.Current`, which
the template does not use. Keep test runs off the user's data the same
way: an `APP_STORE` override that only accepts paths in `%TEMP%`.
`ApplicationData.Current.LocalFolder` is per package, so a test install
under a different package name keeps its own data.

## WPF and WinForms

The design rules in `windows-design` still apply. For a Windows 11 look in
WPF, use the built-in Fluent theme in .NET 9 and later
(`ThemeMode="System"` on the `Application`). WinForms has no Fluent theme:
move the screens one by one to WinUI only if the user asks.
