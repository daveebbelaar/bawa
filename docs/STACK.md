# Is BAWA's stack right? (October 2026)

Checked on 2026-10-29 against Microsoft Learn, the Windows App SDK release
notes, PowerToys' own docs, and the current state of the alternatives.
Sources are numbered and listed at the end. Anything marked
**unverified** came from a secondary source or from memory and was not
confirmed on a primary page.

## Verdict

Yes, the stack is right, for both small productivity apps and a
PowerToys-shaped utility suite. C# on .NET 10 with WinUI (Windows App SDK
2.x) is Microsoft's primary recommendation for new Windows desktop apps
[1], and in 2026 Microsoft rebuilt its own getting-started path around the
same thing BAWA does: the .NET SDK, a terminal, and an AI agent, with no
Visual Studio [2][3][7]. Unpackaged with `WindowsAppSDKSelfContained=true`
is exactly what PowerToys requires for every WinUI module it ships [21],
and it is the default in Microsoft's new Reactor templates [11]. What BAWA
lacks is not a better framework. It is the last mile: the shipped app
still needs the .NET runtime on the target PC, nothing is signed, there is
no installer, updater, or WinGet path, the template does not use NativeAOT,
and there is no route to package identity for the features that need it.
Microsoft also now ships a `winapp` CLI and its own Claude Code plugin for
WinUI [3][8]; BAWA should use the CLI where it saves work and say how it
differs from the plugin. Something else wins only in narrow cases: WPF
for data-grid-heavy business apps today, Avalonia or Uno when one C#
codebase must also run on macOS and Linux, and Tauri when the team only
knows web UI.

## Options at a glance

| Stack | Native look | Startup and memory | Agent-friendliness | Distribution | Verdict |
| --- | --- | --- | --- | --- | --- |
| **C# + WinUI, unpackaged, self-contained WAS (BAWA today)** | Best available: Fluent controls, Mica, `TitleBar`, `SystemBackdropElement` [6] | JIT. Reactor docs measure about 250 ms cold start for a small JIT WinUI app [11]; BAWA's template measured about 1.0 s to first answer, including a 300 ms CLI check. Microsoft admits WinUI RAM use is high and is working on it, no numbers yet [13] | High. `dotnet build` only, text XAML, `x:Bind` compile errors, UIA tree for checks. Microsoft's own docs now target this workflow [2][4] | Folder of files. Needs the .NET runtime unless self-contained or AOT. No identity, so no share target, Windows AI APIs, startup task, or manifest file types [37][38] | **Keep.** Close the shipping gaps below |
| **C# + WinUI + NativeAOT** | Same | Reactor docs: about 50 ms cold start versus 250 ms JIT [11]. PowerToys Command Palette: 40% faster load, 15% less startup memory, about 55% smaller install [22] | Same, plus trim warnings to fix. `dotnet publish` step required | No .NET runtime needed on the target PC | **Adopt for tray utilities and launchers** (Onyx-shaped apps). Optional for windowed apps |
| **C# + WinUI via Reactor (C#-only UI, MVU)** | Same controls, it renders real WinUI [10] | Same as WinUI; AOT template exists [11] | Very high on paper: one C# file per screen, 60+ analyzers, `winapp run app.cs` [10] | Same as WinUI | **Watch, do not adopt.** Experimental, "no support policy, no API guarantees" [10] |
| **WPF on .NET 10 + Fluent theme (`ThemeMode`)** | Good, but Fluent styles are "still in progress" in .NET 10 [30]. Mica and a custom title bar need third-party libraries (WPF UI) (**unverified** for .NET 10 built-ins) | Mature and fast to start. No 2026 head-to-head with WinUI found (**unverified**) | High. Same CLI build, XAML, UIA | Simplest single-file EXE story [37]. Same identity limits as any unpackaged app | **Second choice.** Wins for data-grid-heavy business apps today (WinUI DataGrid is promised "soon" [15]) |
| **WinForms on .NET 10** | Dated look. Dark mode support is recent [1] | Fast, small | Weaker: designer files, little declarative UI | Same as WPF | **Skip** for new BAWA apps |
| **C++/WinRT (WinUI or raw Win32)** | WinUI in C++ has full support [1] | Best: no GC, no JIT | Low. Needs Visual Studio or Build Tools with the C++ workload; `winapp pack` cannot build `.vcxproj` [4] | Same as WinUI | **Only for hot paths**, such as a keyboard hook host, after a measurement says C# is too slow |
| **Avalonia 12** | Own renderer with a Fluent-like theme. Not Windows' own controls, no real Mica | AOT supported on desktop [31] | High. CLI builds, `dotnet watch` hot reload | Cross-platform publish | **Pick only when one codebase must also run on macOS and Linux.** BAWA plus BAMA already covers that with native UI per platform |
| **Uno Platform 6.6** | WinUI API, Skia renderer by default on non-Windows; on Windows it can use real WinUI [32] | NativeAOT on all targets as of 6.6 [32] | High | Cross-platform | Same niche as Avalonia, closer to WinUI's API |
| **.NET MAUI** | On Windows it outputs WinUI 3 [1] | WinUI plus a cross-platform layer on top | Medium. More layers to debug | Same as WinUI | **Skip** for Windows-only apps. Microsoft says pick it only if you target several platforms from day one [1] |
| **React Native for Windows (0.82+)** | Real native controls; Fabric-only since 0.82, WinAppSDK Win32 apps by default [33]. Microsoft rewrites some Start menu parts from React Native to WinUI for speed [14] | JS runtime cost on top | Medium. JS plus native modules, Visual Studio 2026 required for 0.84 [33] | Same as WinUI | **Skip** unless the team is React-first |
| **Tauri 2** | Web UI in WebView2. No Fluent controls | Small install. On Windows, memory is close to Electron because WebView2 is Chromium [34] | High for web devs; Rust for native parts | Own installer; `winapp` can add identity and MSIX [3] | **Only for web-UI teams.** Not native |
| **Electron** | Web UI | Heaviest: about 244 MiB bundle versus 8.6 MiB for Tauri in one measured app (macOS) [34] | High for web devs | Mature updaters | **Skip.** Microsoft is moving its own web-based inbox experiences to WinUI [12][14] |
| **Flutter** | Own renderer, Fluent look through packages | Not measured here (**unverified**) | Medium. Dart, own toolchain | `winapp` supports it [3] | **Skip** for Windows-first apps |

## What real Windows apps use

- **PowerToys** (the model for a tray suite):
  - The runner, `PowerToys.exe`, is C++. It loads each utility as a module
    DLL behind one C++ interface, owns the tray icon, and talks to the
    Settings app over a two-way named pipe with JSON messages [18][19].
  - One low-level keyboard hook (`centralized_kb_hook.cpp`) serves every
    module, so there are not ten hooks in ten processes, plus
    `centralized_hotkeys.cpp` for registered hotkeys [19].
  - A single-instance mutex at startup [19]. Restart-elevated logic in
    `restart_elevated.cpp` [19].
  - Auto start is a scheduled task created per user, which can run with
    highest privileges, not a Run key (seen in `auto_start_helper.cpp`
    through a code mirror; **partly verified**) [19][23].
  - Settings, Peek, Advanced Paste, Image Resizer, Measure Tool and more
    are C# WinUI 3, and the team is moving the rest off WPF (Color Picker
    moved in a 2026 preview) [21][24]. Every WinUI module must set
    `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true` [21].
    Overlays use `WinUIEx.WindowEx` and a `TransparentWindow` base [21].
  - Command Palette switched to NativeAOT in 0.93 [22].
  - Installer: a WiX v5 MSI, per-user by default. Sparse MSIX packages are
    registered only for Windows 11 context menu handlers. Full MSIX was
    abandoned [20].
- **Microsoft Photos**: migrated from UWP to the Windows App SDK and WinUI 3
  [1][26].
- **Windows Terminal**: C++/WinRT, a Win32 host with XAML Islands and
  WinUI 2 [25].
- **Notepad, Paint, Snipping Tool**: Win32 apps with WinUI or Fluent parts
  on top, per 2021 and 2022 press reports. Current internals are
  **unverified**.
- **Windows shell**: Microsoft is rewriting parts of the Start menu in
  WinUI and says it will "move more experiences to WinUI 3" [14].
- **Files (files-community)**: WinUI 3, mostly C#, moved from UWP to the
  Windows App SDK (secondary sources; **partly verified**) [29].
- **DevToys 2**: left UWP for Blazor Hybrid to go cross-platform, after
  rejecting Uno as "a step backward" for its look [27]. Not a native UI.
- **Dev Home**: WinUI 3, discontinued in May 2025 [28]. Not a model.

## What BAWA should change

1. **Ship builds that run on a PC without the .NET SDK.** The template sets
   `SelfContained=false`, so a copy of the app on another PC needs the .NET
   10 runtime. The README's "the .NET SDK is the only install" is true for
   the developer, not for the user. Fix it with NativeAOT (item 2) or a
   self-contained `dotnet publish` for ship builds. Self-contained
   Windows App SDK alone does not cover .NET itself [37].
2. **Make NativeAOT the default publish for tray utilities, launchers, and
   anything that runs all day; make it optional for windowed apps.**
   PowerToys measured 40% faster load and 15% less memory [22]; Reactor's
   docs show about 50 ms versus 250 ms cold start [11]; Microsoft's
   developer page says "Publish with Native AOT for faster startup" [16],
   and WinUI has supported it since Windows App SDK 1.6 [17][45].
   BAWA already uses `LibraryImport`, which is AOT-safe. The Windows
   Community Toolkit supports AOT since 8.2 [44]. Microsoft.Data.Sqlite
   had trim warnings in the past; its .NET 10 status is **unverified**, so
   the skill must publish once and fail on IL2xxx and IL3xxx warnings
   [46]. Rewrite the `performance` skill's ReadyToRun-only advice around
   this, with a measured before and after.
3. **Fix the publish step that drops `.pri` and `.xbf` files.** This is
   why `build.ps1` avoids `dotnet publish` today. Microsoft's unpackaged
   page lists `EnableMsixTooling=true` as a required property for
   unpackaged single-file publish and the build target that checks it
   [37]. Trying `EnableMsixTooling=true` for a normal publish is the first
   fix to test (**unverified** that it alone fixes BAWA's case). AOT,
   self-contained, and an installer all depend on a working publish.
4. **Add a `ship/distribute` skill** (sign, install, update, list):
   - Sign every release. Azure Artifact Signing (formerly Trusted
     Signing) is Microsoft's recommended option outside the Store, about
     $9.99 a month, but individuals must be in the US or Canada
     (organizations: US, Canada, EU, UK). Elsewhere use an OV
     certificate; open-source projects can use SignPath Foundation for
     free. EV no longer skips SmartScreen; reputation builds over time
     with any of them [35][39].
   - Installer and updates: Velopack turns a publish folder into
     `Setup.exe`, a portable zip, and delta updates with `vpk pack` [41].
     Its support for unpackaged WinUI specifically is **unverified**; test
     it on the Snap example.
   - WinGet: a `zip` installer with a `portable` nested installer, created
     and submitted with `wingetcreate` [40].
   - Microsoft Store: an unpackaged app can go in through the MSI/EXE
     path, signed by you, but the Store does not push updates to it.
     An MSIX submission is signed free by the Store [35][36].
5. **Add package identity as an option, through a sparse package.**
   "Packaging with external location" keeps BAWA's folder install and adds
   identity for share targets, startup tasks, context menu extensions,
   manifest file types and protocols, background tasks, and the Windows
   AI APIs [36][38]. `winapp` has a sparse guide and
   `create-debug-identity` for testing [3]. PowerToys uses exactly this for
   its context menus [20]. Update the `os-integrations` table: rows that
   say "not available unpackaged, skip" should say "needs identity: see
   sparse package".
6. **Use the `winapp` CLI where it saves work, and pin its version.** It
   is in public preview [3].
   - `winapp ui inspect | search | invoke | click | set-value |
     screenshot`, plus `--on sandbox` for a clean Windows Sandbox run [4][9].
     This overlaps `scripts/uia.ps1`; keep ours if it is simpler, but
     mention the option.
   - `winapp run --debug-output` decodes WinUI stowed exceptions to the
     failing XAML handler [4]. That covers BAWA's `0xC000027B` launch
     crashes.
   - `winapp find-ui` and `find-api` with `--json` return real WinUI
     Gallery samples and real API names [4]. Call them from `csharp-winui`
     and `windows-design` before writing unfamiliar controls.
   - `winapp pack` and `winapp az-sign` for MSIX and signing [4].
   - `winapp run --detach --json` returns the process ID for scripts [4].
7. **Name Microsoft's own plugin in the README and the router.** The
   `winui@win-dev-skills` plugin gives Claude Code 8 WinUI skills
   (setup, workflow, design, code review, UI testing, packaging, WPF
   migration, session report) [8]. Its default is packaged MSIX with
   Developer Mode on [2][4]. BAWA's difference is the pipeline (clone,
   stories, design kit, grid checks, snapshot and self-test loop) and no
   Developer Mode or MSIX. Say so in one line, so users can install both
   without conflicts.
8. **Correct stale facts in the skills.**
   - `AppPaths.cs` says unpackaged apps have no
     `ApplicationData.LocalSettings`. Windows App SDK 2.2 added
     `ApplicationData.GetForUnpackaged()`, and 2.4 fixed its
     `LocalSettings` path [6]. Keeping `settings.json` is fine (it is
     testable), but fix the comment.
   - `new-windows-app` says MSIX needs "Visual Studio tooling for most
     changes". `winapp pack` now builds a signed MSIX from the command line
     [4]. Keep unpackaged as the default for its seconds-long rebuild loop,
     but change the reason.
   - Notifications: say plainly that Windows App SDK app notifications
     work without identity, and push notifications need it for background
     delivery [38].
9. **Put `AutomationProperties.AutomationId` on every interactive control
   in the template**, and make `uia.ps1` prefer it over the name.
   Microsoft's testing guidance targets controls by AutomationId [9];
   names change when the words change.
10. **Write down the suite architecture for PowerToys-shaped specs.** One
    host process owns the tray icon, the single-instance key, and one
    low-level keyboard hook on its own thread; modules plug into it;
    settings go over a named pipe as JSON [18][19]. Use a scheduled task
    with highest privileges for auto start only when the suite must move,
    hook, or paste into elevated windows; otherwise keep the Run key
    [19]. Default the installer to per-user [20]. Consider WinUIEx for
    overlay and panel windows, as PowerToys does [21].
11. **Offer CsWin32 for new Win32 calls.** Microsoft's FAQ now points C#
    developers to CsWin32 (a `NativeMethods.txt` list, generated
    signatures) [1]. It removes the hand-written signature errors agents
    make. Keep the existing `LibraryImport` code; both are AOT-safe.
12. **Track Windows App SDK servicing.** Self-contained means every app
    carries its own runtime and gets fixes only when rebuilt [37]. The 2.x
    line ships a minor release about monthly (2.0.1 in April to 2.6.4 on
    2026-10-27) and is serviced until 2027-04-29; the next side-by-side
    major will be 3.0 [5][6]. Add an outdated-packages check to
    `build-and-verify`.
13. **Testing: state the tool choice.** WinAppDriver has had no real
    release in years and does not speak W3C WebDriver; vendors are moving
    to FlaUI [42]. If a typed C# UI test suite is ever needed, use FlaUI 5
    (UIA3) [43] or `winapp ui`. Keep xUnit v3 for `Core`. The Appium
    Windows driver's 2026 state is **unverified**.
14. **Watch, do not adopt yet:** Reactor (experimental) [10], and XAML Hot
    Reload outside Visual Studio. Microsoft says Visual Studio 2026 has the
    richest Hot Reload [1]; the state of `dotnet watch` for WinUI in other
    editors is **unverified**. BAWA's rebuild, relaunch, and snapshot loop
    stays the reliable path.

## What BAWA already gets right

- **The framework and language.** C# and WinUI are Microsoft's primary
  recommendation for new Windows-only desktop apps, and C# is its default
  language advice for "most cases" [1]. WPF and WinForms are supported but
  not the first pick for new apps [1].
- **No Visual Studio.** Microsoft's FAQ now says Visual Studio is not
  required, and its quickstart offers a terminal path that "works well for
  agentic coding" [1][2]. BAWA installs the .NET SDK with the same
  `winget` line Microsoft uses [2].
- **Unpackaged, self-contained Windows App SDK.** The same two properties
  PowerToys makes mandatory for every WinUI module [21], and the Reactor
  template default [11]. No Developer Mode needed, unlike packaged dev
  builds [4].
- **A current, pinned SDK.** The template pins Windows App SDK 2.6.4, the
  stable release from 2026-10-27 [5].
- **CommunityToolkit.Mvvm and SettingsControls.** Microsoft's own
  `winui-mvvm` template uses the MVVM Toolkit [4], and PowerToys' Settings
  app is the reference for the settings look.
- **A `Core` library with no WinUI, tested by xUnit.** It keeps logic
  testable without a window and makes AOT trimming easier to reason about.
- **OS integration choices that match PowerToys.** `RegisterHotKey` for
  plain combos, a low-level hook on its own thread for hold-to-show and
  remapping, a hidden window for the tray icon, `AppInstance` for a single
  copy, and named pipes for a CLI [18][19].
- **Notifications through `AppNotificationManager`**, which works without
  identity [38].
- **x64 and ARM64 builds.** Microsoft recommends native Arm64 [1].
- **Verification an agent can read.** UI Automation checks, snapshots,
  self-tests, and a layout probe point the same way Microsoft's new
  `winapp ui` and testing guidance point [9].
- **`LibraryImport` for P/Invoke**, which is source-generated and
  AOT-safe.
- **Build output outside the repo** and a one-command build and install.

## Sources

1. Microsoft Learn, Windows developer FAQ (2026-10-02): https://learn.microsoft.com/en-us/windows/apps/get-started/windows-developer-faq
2. Microsoft Learn, Build your first WinUI app (2026-10-13): https://learn.microsoft.com/en-us/windows/apps/get-started/start-here
3. Microsoft Learn, Windows App Development CLI (winapp) (2026-10-08): https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/
4. Microsoft Learn, Using winapp CLI with WinUI (2026-10-13): https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/winui
5. Microsoft Learn, Windows App SDK release channels (2026-10-27): https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/stable-channel
6. Microsoft Learn, Windows App SDK 2.x release notes: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
7. Microsoft Learn, AI-assisted Windows development (2026-10-08): https://learn.microsoft.com/en-us/windows/apps/develop/ai-assisted/
8. Microsoft Learn, WinUI agent plugin for GitHub Copilot CLI and Claude Code (2026-07-05): https://learn.microsoft.com/en-us/windows/apps/develop/ai-assisted/winui-agent-plugin
9. Microsoft Learn, AI-assisted testing for Windows apps (2026-10-02): https://learn.microsoft.com/en-us/windows/apps/develop/ai-assisted/testing
10. Windows Developer Blog (ifdef windows), Microsoft.UI.Reactor: a C#-first way to build WinUI apps (2026-10-21): https://devblogs.microsoft.com/ifdef-windows/?p=1436
11. Microsoft.UI.Reactor docs, Packaging: https://microsoft.github.io/microsoft-ui-reactor/latest/packaging/
12. Windows Latest, Reactor coverage (2026-10-23): https://www.windowslatest.com/2026/10/23/microsoft-is-tired-of-web-app-slop-on-windows-11-and-now-its-making-native-winui-apps-much-easier-to-build/
13. Windows Latest, WinUI memory (2026-07-27): https://windowslatest.com/2026/07/27/microsoft-admits-windows-11-native-apps-hog-ram-promises-a-winui-performance-boost-before-start-menu-rewrite
14. Open Source For You, WinUI goes more open source (2026-06-05): https://www.opensourceforu.com/2026/06/winui-goes-more-open-source-as-microsoft-rebuilds-windows-11/
15. MESCIUS, What Build 2026 means for Windows developers: https://developer.mescius.com/blogs/microsoft-recommitted-winui-what-build-2026-means-for-windows-developers
16. Microsoft Developer, Build native Windows apps with WinUI: https://developer.microsoft.com/en-us/windows/apps/build-a-windows-app
17. Windows Developer Blog, What's new in Windows App SDK 1.6 (Native AOT): https://blogs.windows.com/windowsdeveloper/2024/09/04/whats-new-in-windows-app-sdk-1-6/
18. PowerToys dev docs, Architecture: https://microsoft.github.io/PowerToys/core/architecture/
19. PowerToys dev docs, Runner: https://microsoft.github.io/PowerToys/core/runner/
20. PowerToys dev docs, Installer: https://microsoft.github.io/PowerToys/core/installer/
21. PowerToys repo skill, WPF to WinUI 3 migration (mirrored): https://tessl.io/registry/skills/github/microsoft/PowerToys/wpf-to-winui3-migration
22. Windows Command Line Blog, PowerToys 0.93 (2025-08-13): https://devblogs.microsoft.com/commandline/powertoys-0-93-is-here-faster-command-palette-new-dashboard-ux-and-more/
23. PowerToys GitHub repository: https://github.com/microsoft/PowerToys
24. warp2search, PowerToys 0.101 preview moves Color Picker to WinUI 3: https://www.warp2search.net/story/microsoft-powertoys-v010124920-preview-migrates-color-picker-to-winui-3-and-adds-multizone-clocks/
25. Windows Developer Blog, Building Windows Terminal with WinUI (2020): https://blogs.windows.com/windowsdeveloper/2020/09/08/building-windows-terminal-with-winui/
26. Windows Developer Blog, Microsoft Photos: migrating from UWP to Windows App SDK (2024): https://blogs.windows.com/windowsdeveloper/2024/06/03/microsoft-photos-migrating-from-uwp-to-windows-app-sdk/
27. DevToys blog, The journey to DevToys 2.0: https://devtoys.app/blog/the-journey-to-devtoys-2.0
28. Techzine, Microsoft discontinues Dev Home: https://www.techzine.eu/news/devops/128233/microsoft-discontinues-windows-dev-home/
29. Files community, Announcing Files v3: https://files.community/blog/posts/v3
30. Microsoft Learn, What's new in WPF for .NET 10 (2026-02-10): https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100
31. Avalonia, Avalonia 12 and Native AOT docs: https://avaloniaui.net/blog/avalonia-12 and https://docs.avaloniaui.net/docs/deployment/native-aot
32. InfoQ, Uno Platform 6.6 (2026-08): https://infoq.com/news/2026/08/uno-platform-6-6
33. React Native DevBlog, React Native Windows v0.82 (2026-04-03): https://devblogs.microsoft.com/react-native/%f0%9f%9a%80react-native-windows-v0-82-is-here/
34. Hopp, Tauri vs Electron measured: https://dev.to/gethopp/tauri-vs-electron-performance-bundle-size-and-the-real-trade-offs-1el4 and Tauri issue on WebView2 memory: https://github.com/tauri-apps/tauri/issues/5889
35. Microsoft Learn, Code signing options (2026-08-29): https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
36. Microsoft Learn, Packaging overview (2026-08-29): https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/
37. Microsoft Learn, Distribute an unpackaged WinUI 3 app (2026-10-02): https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
38. Microsoft Learn, Features that require package identity (2026-10-03): https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/modernize-packaged-apps
39. Azure, Artifact Signing: https://azure.microsoft.com/en-us/products/artifact-signing and DevClass (2026-01-14): https://www.devclass.com/security/2026/01/14/code-signing-windows-apps-may-be-easier-and-more-secure-with-new-azure-artifact-service/4079554
40. Microsoft Learn, WinGet manifest and repository submission: https://learn.microsoft.com/windows/package-manager/package/manifest and https://learn.microsoft.com/windows/package-manager/package/repository
41. Velopack docs, Packaging overview: https://docs.velopack.io/packaging/overview
42. Microsoft Q&A, Is WinAppDriver dead?: https://learn.microsoft.com/en-us/answers/questions/1455246/is-the-tool-winappdriver-dead-or-not and Katalon's FlaUI-based driver note: https://docs-production-katalon.netlify.app/katalon-platform/troubleshooting/troubleshooting-common-execution-issues/windows-desktop-app-testing-unavailable-in-katalon-studio-10.x
43. NuGet, FlaUI.UIA3 5.0.0: https://www.nuget.org/packages/FlaUI.UIA3/
44. Visual Studio Magazine, Windows Community Toolkit 8.2 adds Native AOT support: https://visualstudiomagazine.com/Articles/2025/04/03/Windows-Community-Toolkit-8-2-Adds-Native-AOT-Support.aspx
45. Microsoft Learn, .NET Native AOT deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
46. dotnet/efcore issue 29725, Microsoft.Data.Sqlite AOT warnings: https://github.com/dotnet/efcore/issues/29725
