---
name: distribute
description: Ship a Windows app to other PCs, from a self-contained or NativeAOT publish to signing, a zip or installer, WinGet, the Microsoft Store, updates, and package identity through a sparse package. Use when the user wants to share the app, make a release, sign it, get past SmartScreen, publish to WinGet or the Store, or needs a feature that requires package identity.
argument-hint: "[version]"
---

# Distribute

`build.ps1` installs a build that only runs on a PC with the .NET runtime.
Everything here starts from `tools/publish.ps1`, which makes a build that
runs anywhere, and checks it with the self-test before you ship it.

## 1. Publish

```powershell
tools/publish.ps1            # self-contained: .NET and the Windows App SDK in the folder
tools/publish.ps1 -Aot       # NativeAOT: native code, no JIT
tools/publish.ps1 -Aot -Zip  # plus a release zip
```

Measured on the template (Windows 11, 125% display, cold start to the main
window):

| Kind | Start | Folder | Needs to build |
| --- | --- | --- | --- |
| Self-contained (JIT) | about 420 ms | 236 MB | the .NET SDK |
| NativeAOT | about 170 ms | 173 MB, a 9 MB exe | the .NET SDK and the MSVC linker |

Pick NativeAOT for anything that runs all day or opens on a shortcut: tray
utilities, launchers, panels. PowerToys moved its Command Palette to
NativeAOT for the same reason. Pick self-contained when the machine that
builds has no C++ tools.

The MSVC linker comes from Visual Studio Build Tools, not the IDE:

```powershell
winget install Microsoft.VisualStudio.2022.BuildTools --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
```

`publish.ps1` fails on any trim or AOT warning (`IL2xxx`, `IL3xxx`): each
one is a place that may break only in the published app. The usual fixes:

| Warning about | Fix |
| --- | --- |
| `JsonSerializer.Serialize<T>` or `Deserialize<T>` | Source-generated JSON: add the type to `AppJson` (`[JsonSerializable(typeof(T))]`) and pass `AppJson.Default.T` |
| `Activator.CreateInstance`, `Assembly.GetTypes()` | A static list of the types, or `[DynamicallyAccessedMembers]` on the `Type` |
| A NuGet package | Update it; check its docs for AOT support; otherwise publish without `-Aot` |

Gotchas:

- `dotnet publish` of an unpackaged WinUI app drops the app's `.pri` (the
  compiled XAML lives in it) unless `EnableMsixTooling=true` is set. The
  app then dies in `InitializeComponent` with no message. `publish.ps1`
  sets it and checks the `.pri` is there.
- `SelfContained=false` in the project blocks NativeAOT
  (`NETSDK1102`). `publish.ps1` passes `--self-contained true`.
- NativeAOT's link step looks for `vswhere.exe` on the PATH and fails with
  "'vswhere.exe' is not recognized" even when Build Tools are installed.
  `publish.ps1` adds the Visual Studio Installer folder to the PATH.

Check: `publish.ps1` prints `published ...` and the self-test of the
published app passes.

## 2. Sign

Unsigned apps get a SmartScreen warning ("Windows protected your PC") and
some company PCs refuse them. Sign the exe and any installer. Options:

| Who | Option |
| --- | --- |
| Individuals in the US or Canada, organizations in the US, Canada, EU, UK | Azure Artifact Signing (formerly Trusted Signing), about 10 USD a month |
| Everyone else | An OV code signing certificate from a CA |
| Open source projects | SignPath Foundation, free |

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a "$out\<App>.exe"
signtool verify /pa "$out\<App>.exe"
```

An EV certificate no longer skips SmartScreen. Reputation builds as people
download the signed file, with any of these. Signing tools and accounts
are the user's: ask before you sign, and never put a certificate password
in a script or a file in the repo.

## 3. Package

| Shape | When | How |
| --- | --- | --- |
| Zip | A first release, a portable app, WinGet | `publish.ps1 -Zip` |
| Installer with updates | People expect Start menu, uninstall, and updates | Velopack: `vpk pack` turns the publish folder into `Setup.exe`, a portable zip, and delta updates. Test it on your app before you rely on it |
| MSI | Company deployment | WiX, per-user by default, like PowerToys |
| MSIX | The Store with free signing, or features that need identity | `winapp pack` (Microsoft's `winapp` CLI) builds and signs an MSIX from the command line |

Default to per-user installs in `%LOCALAPPDATA%\Programs\<App>`, the same
path `build.ps1` uses, so nothing needs administrator rights.

## 4. List

- **GitHub release**: the zip, its SHA256 (`Get-FileHash`), and the
  changes in words people use.
- **WinGet**: a manifest with a `zip` installer and a `portable` nested
  installer, made and submitted with `wingetcreate new <zip url>`. People
  then run `winget install <you>.<App>`.
- **Microsoft Store**: an unpackaged app goes in through the MSI or EXE
  route, signed by you; the Store does not update it for you. An MSIX
  submission is signed by the Store for free.

## 5. Package identity, only when needed

Some Windows features work only for an app with package identity: share
targets, startup tasks, Explorer context menu entries on Windows 11, file
types and protocols declared in a manifest, background tasks, and the
Windows AI APIs. Global hotkeys, the tray, toasts from the Windows App SDK,
and the HKCU Run key work without it.

When a story needs one of those, keep the folder install and add a sparse
package ("packaging with external location"): a small signed MSIX that
gives the existing exe an identity. PowerToys does this for its context
menus. `winapp` has a guide and `create-debug-identity` for testing.

## Check before a release

1. `tools/publish.ps1 -Aot -Zip` passes, with the self-test.
2. The zip runs on a PC (or a Windows Sandbox) with no .NET installed:
   unzip, start, check the main page.
3. The exe is signed and `signtool verify /pa` passes.
4. Version and changes are in the release notes.

Done when the published build passes its self-test, runs on a clean PC,
is signed or the user decided to ship unsigned, and the release (zip,
installer, or manifest) is where the user wanted it.
