# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

SwiftClean is a Windows desktop PC-cleaning utility built on **WPF**, targeting **.NET 8** (`net8.0-windows`). The UI is a faithful port of the imported design `SwiftClean.dc.html` (claude.ai/design). All nine pages exist (Dashboard, Cleaning/Очистка, Registry/Реестр, Startup/Автозагрузка, Apps/Приложения, Disk/Диск, Drivers/Драйверы, Scheduler/Планировщик, Settings/Настройки). The UI is bilingual (English default, Russian — see Localization). Every page is wired to real Windows data (nothing is mock).

Key project settings (`SwiftClean.csproj`): `Nullable` enabled, `ImplicitUsings` enabled, `UseWPF=true`. Root namespace is `SwiftClean`.

## Commands

Run from the repository root (the directory containing `SwiftClean.slnx`).

```bash
dotnet build                      # build
dotnet run --project SwiftClean.csproj   # build and launch the app
dotnet clean                      # remove build artifacts
dotnet test                       # run the unit tests (tests/SwiftClean.Tests)
dotnet test --filter "FullyQualifiedName~<TestName>"   # run a single test
```

Tests live in `tests/SwiftClean.Tests/` (xUnit, `net8.0-windows`, `UseWPF`), referenced from `SwiftClean.slnx`; they cover the pure/Windows-independent helpers (`SizeFormatter`, `Loc` EN/RU key+placeholder parity, `StringToBrushConverter`). Because the test folder is *inside* the main project's directory, `SwiftClean.csproj` excludes it from its default globs via `<DefaultItemExcludes>…;tests\**</DefaultItemExcludes>` — keep new test sources under `tests\`. The `Service` classes touch the real registry/filesystem, so they're not unit-tested yet (would need an abstraction layer). No linter is configured.

## Intended architecture (MVVM)

The empty folders in `SwiftClean.csproj` define the intended structure — place new code accordingly:

- `Views/` — XAML windows/user controls (UI only).
- `ViewModels/` — view-model classes holding state and commands; the data-binding target for Views.
- `Models/` — domain/data types.
- `Services/` — the actual cleaning logic (disk scanning, file deletion, registry/temp cleanup, etc.), injected into view models rather than called from UI code-behind.
- `Helpers/` — shared utilities.
- `Resources/Styles/` — XAML resource dictionaries (themes, control styles).

`App.xaml` sets `StartupUri="MainWindow.xaml"` and merges `Resources/Styles/Colors.xaml` into `Application.Resources` — register further app-wide resource dictionaries there. Keep code-behind (`*.xaml.cs`) thin; logic belongs in view models and services.

## Installer (`installer/SwiftClean.Installer`)

A **separate WPF project** (not referenced by the app — it carries the app as an embedded payload) that is a 1:1 port of the imported design `SwiftClean Installer.dc.html` (also from claude.ai/design, same project as `SwiftClean.dc.html`). It is a 5-step wizard (Welcome → License → Path → Installing → Done) in the same dark theme, plus a standalone `/uninstall` flow. Manifested `requireAdministrator` (installs to `C:\Program Files\SwiftClean`, writes `HKLM`).

- **Build:** `./build-installer.ps1` from the repo root → (1) publishes the app self-contained `win-x64`, (2) zips it to `installer/SwiftClean.Installer/Resources/payload.zip`, (3) publishes the installer as a single self-contained exe → `dist/SwiftCleanSetup.exe` (~133 MB). The payload, `dist/`, and `installer-stage/` are gitignored. The installer csproj embeds `payload.zip` *conditionally* (`Exists(...)`), so it compiles without it (UI works; install throws a clear "no payload" error). `InstallerService.HasPayload` reflects this.
- **Wiring:** mirrors the app — single `InstallerViewModel` holds all state/commands; `MainWindow.xaml` is the borderless 720×500 shell (stepper sidebar + content + bottom nav). Self-contained `Helpers/` (RelayCommand, ObservableObject, converters, `ShellLink` COM shortcut creator) and `Resources/Styles.xaml` (own copy of the colour tokens + the app logo recreated as a XAML `DrawingImage` vector from `SwiftClean_Icon.svg`). **No reference to `SwiftClean.csproj`.**
- **Real actions** (`Services/InstallerService.cs`): extracts the payload (zip-slip-guarded) with live progress, drops a copy of the setup exe as `uninstall.exe`, creates desktop + Start-Menu `.lnk` shortcuts (`ShellLink`/`IShellLink`), writes the `HKLM\…\Uninstall\SwiftClean` entry (`UninstallString = "uninstall.exe" /uninstall`, `QuietUninstallString` adds `/silent`), and `version.dat`. Launch-after-install is optional. Uninstall removes shortcuts + registry, then self-deletes the install folder via a detached `cmd … rmdir` (the running uninstaller lives inside it). `App.OnStartup` branches on `/uninstall` (+`/silent` = headless).
- **Gotcha:** `ProgressBar.Value` (RangeBase) binds **TwoWay by default** — bind it `Mode=OneWay` to read-only VM properties or it throws at window load. The main `SwiftClean.csproj` excludes sibling project dirs from its globs via `<DefaultItemExcludes>…;tests\**;installer\**`.

## Architecture (how the UI is wired)

The design is one component, so the app mirrors that: a **single `MainViewModel`** holds *all* application state and commands, and **every page is a `Views/*View.xaml` `UserControl`** placed together in the `MainWindow` content area, each shown/hidden by a `Visibility` binding. Page UserControls have **no own DataContext** — they inherit `MainViewModel` from the window, so a page binds directly to e.g. `{Binding CleanItems}`, `{Binding ScanCommand}`.

- `MainWindow.xaml` — the shell: borderless window (`WindowStyle=None` + `WindowChrome`), sidebar, top bar, content host, status bar, scan modal. Code-behind is window-chrome only (drag, min/max/close, taskbar-aware maximize).
- `MainViewModel.cs` — navigation, scan flow, and the data/commands for **all** pages. Page switching is `_activePage` (string id) + `Is<Page>` bool properties (e.g. `IsDashboard`).
- `Views/*View.xaml` — one UserControl per page, bound to `MainViewModel`.
- `Resources/Styles/Colors.xaml` — every color/brush/gradient (see token table below).
- `Resources/Styles/Controls.xaml` — every shared control style + the two converters; merged app-wide in `App.xaml`.
- Infra: `Helpers/RelayCommand.cs`, `Helpers/StringToBrushConverter.cs`, `Helpers/SizeFormatter.cs`, `ViewModels/ViewModelBase.cs`, models in `Models/`, `Services/ScannerService.cs`.

**Adding a page:** (1) add a `NavigationItem` to `BuildNavigation()` with id/title/icon/section; (2) add an `Is<Page>` bool + a `PageTitle` switch case + raise it in `RaisePageFlags()`; (3) create `Views/<Page>View.xaml(.cs)` bound to `MainViewModel`; (4) add it to the content `Grid` in `MainWindow.xaml` with `Visibility="{Binding Is<Page>, Converter={StaticResource BoolToVis}}"`. Put page data/commands on `MainViewModel`.

---

# Design system

Dark theme only. **Never inline a hex value or a one-off size** — use the resource keys below. New colors go in `Colors.xaml`, new reusable control styles in `Controls.xaml`. This is a 1:1 reference for `SwiftClean.dc.html`; match it when adding features.

## Color tokens (`Resources/Styles/Colors.xaml`)

All are `SolidColorBrush` resources unless noted. Reference as `{StaticResource <Key>}`.

**Surfaces**
| Key | Hex | Use |
|-----|-----|-----|
| `BackgroundBrush` | `#0f0f11` | window / content / topbar background, inset field bg |
| `SidebarBrush` | `#0a0a0c` | sidebar + status bar |
| `CardBrush` | `#141418` | panels, cards, modal, nav active row |
| `RowHoverBrush` | `#18181d` | hovered list/nav row |
| `BorderBrush` | `#1e1e22` | standard 0.5px borders / panel outlines |
| `DividerBrush` | `#1a1a1e` | inner list-row dividers |
| `BorderLightBrush` | `#2a2a35` | lighter border (modal, search box, empty-state icons, check border) |
| `TrackBrush` / `ToggleOffBrush` | `#1e1e28` | progress-bar track, toggle-off track |

**Accent & status**
| Key | Hex | Use |
|-----|-----|-----|
| `AccentBrush` | `#5b4fff` | primary actions, active nav, selection, fills |
| `AccentLightBrush` | `#8b6fff` | accent text (badges, support card) |
| `AccentHoverBrush` | `#4a40d8` | accent button hover |
| `BlueBrush` | `#5b7fff` | secondary blue accent |
| `ErrorBrush` | `#e05c5c` | errors, destructive, "Удалить" |
| `SuccessBrush` | `#4ab87d` | success/done, status dot |
| `WarningBrush` | `#d4924a` | warnings, medium impact |

**Soft tints (pills / icon backgrounds)**
| Key | Hex | Use |
|-----|-----|-----|
| `SuccessSoftBrush` / `SuccessBorderBrush` | `#0f1f17` / `#264f3a` | "done" pill |
| `ErrorSoftBrush` / `ErrorBorderBrush` | `#1f1414` / `#4f2a2a` | error pill, red icon square |
| `WarnSoftBrush` | `#1f1a10` | amber icon square |
| `BlueSoftBrush` | `#101828` | blue icon square |
| `BadgeBgBrush` | `#1e1a2e` | nav count badge, version chip |
| `SupportBrush` / `SupportBorderBrush` | `#1a1630` / `#2d2650` | "Поддержать" card |

**Text** (light → faint)
| Key | Hex | Use |
|-----|-----|-----|
| `TextBrightBrush` | `#e8e8ec` | primary text, titles, values |
| `TextMutedBrush` | `#b8b8c8` | secondary text, list item names, panel titles |
| `TextFaintBrush` / `NavInactiveTextBrush` | `#6b6b78` | tertiary text, idle nav |
| `StatLabelBrush` | `#45454f` | uppercase labels, descriptions |
| `SectionLabelBrush` | `#38383f` | faintest captions, sidebar section headers, "not scanned" |
| `NavActiveTextBrush` | `#c4b8ff` | active nav item text/icon |

**Gradients & scrim**
- `LogoGradientBrush` — diagonal `#5b4fff → #8b6fff` (logo badge, about icon).
- `ScanFillBrush` — horizontal `#5b4fff → #8b6fff` (scan progress fill).
- `ScanOverlayBrush` — `#E60A0A0C` (= `rgba(10,10,12,0.9)` scan-modal scrim).

*(Legacy keys `TextPrimaryBrush #f2f2f5`, `TextSecondaryBrush #8a8a94`, `HoverBrush #16161a` still exist — prefer `TextBright`/`TextMuted`/`RowHover`.)*

## Typography

Font: **Segoe UI** everywhere (set on `Window`/`UserControl`). Weight `Medium` = the design's `font-weight:500`; use it for emphasis (titles/values), not `Bold`.

| Role | Size | Weight | Brush |
|------|------|--------|-------|
| Page title | 15 | Medium | `TextBright` |
| Stat value | 22 | Medium | status color (greys to `SectionLabel` pre-scan) |
| Summary value | 20 | Medium | status color |
| Panel title | 13 | Medium | `TextMuted` |
| Body / item name | 13 | — | `TextMuted` |
| Secondary / size | 12 | — | `TextFaint` / status |
| Caption / description | 11 | — | `StatLabel` / `SectionLabel` |
| Uppercase label | 11 | Medium | `StatLabel` (use `ColLabel` style) |
| Section header | 10 | Medium | `SectionLabel` (uppercase, sidebar) |
| Badge / version chip | 10 | — | `AccentLight` / `StatLabel` |
| Mono (paths, time) | 11 | — | `Consolas` font, `SectionLabel`/`TextMuted` |

## Shape, spacing, borders

- **Radii:** panel/card `10`; modal `12`; button/chip/search-box `7`–`8`; icon square `7`; field box `6`; badge `10` (count) / `4` (chip); pill toggle `11`; check box `4`.
- **Borders:** `0.5px` standard (`BorderBrush`); `1px` only on toggle/checkbox outlines; row dividers use `DividerBrush`.
- **Spacing:** content padding `20,16`; panel padding `14` or `16`; list row padding `14,10` (dense) or `14,13`; card-to-card gap `12`; stat-grid inner gap `10`; small inline gaps `6`–`8`.
- **Icon square (clean rows):** `30×30`, radius `7`, bg = soft tint, glyph `14px` in status color.

## Iconography

Icons are **Segoe MDL2 Assets** glyphs (`FontFamily="Segoe MDL2 Assets"`).
- In **XAML** use the XML entity: `Text="&#xE721;"`.
- In **C#** use a unicode escape (backslash-u + the 4-hex code, e.g. the search glyph is `U+E721`). **Do not paste raw glyph characters into source** - the editor strips them; if a literal char is unavoidable, inject it via PowerShell `[char]0xE721`.

Glyphs in use: nav Home `E80F`, Trash `E74D`, Database `EBD2`, Play `E768`, AllApps `E71D`, Storage `EDA2`, Calendar `E787`, Settings `E713`; Search/scan `E721`, Refresh/spinner `E72C`, Check `E73E`, Globe `E774`, Page `E7C3`, Document `E8A5`, Folder `E8B7`, Heart `EB51`, Tools `E90F`, Moon `EC46`, Sun `E706`; logo star `&#x2726;` (`✦`). Chrome buttons: min `E921`, max `E922`, close `E8BB`.

## Shared control styles (`Resources/Styles/Controls.xaml`)

Reuse these — don't re-template:

| Key | Type | What it is |
|-----|------|-----------|
| `Panel` | Border | card/panel: `CardBrush` bg, `0.5px BorderBrush`, radius 10 |
| `PanelTitle` | TextBlock | 13 Medium `TextMuted` panel heading |
| `ColLabel` | TextBlock | 11 Medium `StatLabel` uppercase column/stat label |
| `AccentButton` | Button | accent action button, radius 7, hover→`AccentHover`; set `Content`/`Padding` per use |
| `GhostButton` | Button | secondary button: `CardBrush` bg, `BorderLight` outline, `TextMuted`, hover `RowHover` (dialog cancel, etc.) |
| `PillToggle` | ToggleButton | 38×22 switch; track→`Accent` + thumb slides right when `IsChecked` |
| `BoxCheck` | ToggleButton | 16×16 square check; `Accent` bg + `E73E` check when `IsChecked` |
| `NavItem` | Button | sidebar row (icon + label + badge), 2px left accent + `CardBrush` when `IsSelected`, hover `RowHover`. DataContext = `NavigationItem` |
| `MiniBar` | ProgressBar | 6px rounded percentage bar; `Foreground` = fill color, track `TrackBrush` |
| `Chevron` | ToggleButton | expander caret (`E76C`); rotates 0→90° (180ms) and recolors `SectionLabel`→`AccentLight` when `IsChecked` |
| `FilePreview` | Border | expanding container under a row; animates `MaxHeight` 0→160 (220ms `CubicEase`), `ClipToBounds`, bg `Background`, top border `Divider`. Driven by a `{Binding IsExpanded}` DataTrigger |
| `StringToBrush` | converter | hex string → brush (for per-item colors from view models) |
| `BoolToVis` | converter | `BooleanToVisibilityConverter` (page/empty-state toggling) |

There is also an **implicit `ScrollBar` style** (no key) in `Controls.xaml` — a 4px-wide thin scrollbar (transparent track, thumb `#1e1e22`, hover `#2a2a35`) applied app-wide. Don't re-template scrollbars; rely on it. For scrollable areas set `VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"`.

Page/window-local styles worth reusing as patterns: `ScanBar` (4px gradient bar, MainWindow), `ChromeButtonStyle`/`CloseButtonStyle` (window buttons), `StatRed`/`StatAmber` (Dashboard stat that greys pre-scan), `RegRow`/`FieldLabel`/`FieldBox`/`FieldText` (Registry), `FreqChip` (Scheduler segmented selector via `Tag`), `Legend` (Disk).

## Component recipes

- **Panel** = `<Border Style="{StaticResource Panel}">`; header row inside = nested `Border` with `BorderThickness="0,0,0,0.5" BorderBrush="{StaticResource BorderBrush}"` + `PanelTitle`.
- **List row** = `Border` divider bottom (`DividerBrush`, `0,0,0,0.5`), padding `14,10`; hover `RowHover`. Icon square + name/desc stack + right-aligned size + `BoxCheck`.
- **Expandable clean row** (Dashboard + Cleaning use the same `CleanItem`): wrap the row `Grid` + a `FilePreview` border in a divider `Border`>`StackPanel`. Row gets a `Chevron` (column before `BoxCheck`, shown only via `HasFiles`) bound `IsChecked={Binding IsExpanded}`. Inside `FilePreview`: `ScrollViewer` (vert Auto / horiz Disabled) → file rows (`Border` divider `RowHover`, padding `54,5,14,5` to indent under the category icon: small `SectionLabel` file glyph + name `TextFaint` ellipsis + size, `ToolTip`=full path) → italic `StatLabel` "…и ещё N файлов" gated by `HasMore`. Opening one row collapses the others (accordion, handled in `MainViewModel.OnCleanItemChanged`).
- **Stat / summary card** = `Panel`, padding 14, `ColLabel` + value (22/20 Medium) + caption (`SectionLabel`).
- **Empty state** = centered MDL2 glyph (32–40px, `BorderLightBrush`) + 1–2 lines (`StatLabel`/`SectionLabel`). Toggle with `NoScanData`.
- **Badge pill** = `Border` radius 10, `BadgeBgBrush`, 10px `AccentLight` text; show via `HasBadge`.
- **Done pill** = `SuccessSoftBrush`/`SuccessBorderBrush`, `E73E` + label in `Success`.
- **Master/detail** = fixed-width list column + `*` detail (Registry: `296`/`*`); rows are command buttons (`SelectRegCommand`), selected/hover via triggers.
- **Table** = header `Border` (`ColLabel`s in a `Grid` with the same `ColumnDefinitions` as rows) + `ItemsControl` rows.
- **Toggle switch / checkbox** — always `PillToggle` / `BoxCheck`, bound `IsChecked` two-way.
- **Confirm dialog** — use the in-app overlay, **never** `MessageBox`. `MainViewModel.ShowConfirmAsync(title, message, confirmText, cancelText)` returns `Task<bool>` (via `TaskCompletionSource`); it sets `DialogTitle/Message/ConfirmText/CancelText` + `IsDialogVisible`, and `DialogConfirmCommand`/`DialogCancelCommand` resolve it. The overlay lives in `MainWindow.xaml` (scrim + 12-radius card, `GhostButton` cancel + `AccentButton` confirm). Reuse it for any yes/no prompt.

## Layout conventions

- **Content host:** `ScrollViewer Padding="20,16"` wrapping a `Grid` whose `MinHeight` binds to the scroller's `ViewportHeight` (so full-height pages fill, tall pages scroll). All page UserControls live in this Grid.
- **Page widths:** two-column pages fill; single-column pages are `HorizontalAlignment="Left"` with `MaxWidth` — Cleaning `660`, Startup/Apps `740`, Scheduler/Settings `560`.
- **Column splits:** Dashboard `* / 280`, Registry `296 / *`, Disk `* / 264`; inter-column gap `12` (via margins).
- **Action buttons** are content-sized (`Padding="12,7"`/`16,7`), never stretched — except true full-width panel-footer buttons (e.g. "Очистить …").

## Russian UI

All user-facing strings are Russian (e.g. "Сканировать", "Найденный мусор", "Очистить", "Исправить ключ"). Page ids/keys stay English in code; `PageTitle` and labels are Russian. Keep new strings Russian and consistent in tone with existing ones.

## Destructive operations

This is a system-cleanup tool: real cleaning/registry/startup actions touch the filesystem and OS. Keep them in `Services/` (like `ScannerService`, which only *measures*), behind confirmation, and prefer Recycle Bin / dry-run over hard deletes from UI handlers.

## State of the data

The scan also collects a **file sample** per category (up to `ScannerService.SampleCap` = 500 `FileEntry` items) → surfaced on `CleanItem.Files`/`FileTotal`/`HasMore`/`MoreText` (model `CleanFile`) and shown in the expandable row preview. The **Recycle Bin** category is special-cased: its size/count come from the shell (`SHQueryRecycleBin`), not a directory walk (a raw walk over-counts the per-SID `desktop.ini` files).

**Browser Cache** is per-browser: `ScannerService.BrowserSpecs()` lists known browsers (Chrome/Edge/Brave/Opera/Yandex/Vivaldi/Firefox), and the scan emits a `BrowserCacheInfo` per installed one (cache folder exists) → `ScanCategory.Browsers`. On the `CleanItem` these become selectable `BrowserCache` models, shown as checkboxes in the row's expander (instead of files; `ShowFiles` vs `HasBrowsers` pick which). For a browser item, `CleanItem.Bytes`/`DisplaySize`/`Desc` are computed from the **selected** browsers. Cleaning passes only the chosen browsers' paths (`category with { Paths = ... }`); after cleaning, only the selected browsers are zeroed. NB: browser cache barely shrinks while the browser is running (files are locked and regenerated) — the row shows a "закройте браузер" hint.

The post-clean refresh is in-place (`ApplyCleanResult`): cleaned categories are zeroed (or, for browsers, the selected sub-items), and `CleanedSummary` (captured pre-clean) feeds the success plate. After cleaning, a second in-app `ShowConfirmAsync` offers to open the Recycle Bin (`explorer shell:RecycleBinFolder`) — except when **only** the Recycle Bin was cleaned (it's emptied permanently, so the wording differs and no open-prompt is shown).

The scan modal checklist is real too: `MainViewModel.ScanStages` (model `ScanStage`, keyed to the scanner's category names) is advanced from `IProgress<ScanProgress>` — each stage goes pending → active (spinner) → done (check).

**Startup** is real (`StartupService`): it reads Run keys (HKCU/HKLM/WOW6432Node) + the per-user/common Startup folders, derives publisher/size from the target exe, and toggles entries the Task-Manager way via the `StartupApproved` registry values (no Run value is deleted). HKLM writes need admin — `SetEnabled` returns false on failure and the VM reverts the toggle and shows an admin-needed dialog. Impact/boot-delay columns are a size-based **heuristic** (`ImpactFor`/`EstimateStartupMs`), since Windows doesn't expose the real values.

**Apps** is real (`AppsService`): installed programs from the Uninstall keys (HKLM, HKLM\WOW6432Node, HKCU) — skipping OS components/updates — with name/publisher/size/install-date and a best-effort **last-used** date (from the ROT13-encoded `UserAssist` registry, matched by install folder). The page has sortable headers (`SortAppsCommand`, arrow via `SortArrow`/`IsSort*`), a storage stats panel (total app size + free C:), and a refresh button. "Удалить" (`UninstallAppCommand`) confirms, launches the program's own uninstaller via `ProcessStartInfo{UseShellExecute=true}` (so it can elevate), then `await`s exit and refreshes the list/stats.

**Browser Cookies** is a separate clean category alongside Browser Cache — same per-browser selection mechanism (`ScanBrowsersAsync` handles both cache folders and cookie *files*; `CleanerService.RecycleContents` recycles a single file or a folder's contents).

**Registry** is real (`RegistryService`): finds leftover Uninstall entries whose `InstallLocation` folder no longer exists (apps removed without cleanup), surfaced as `RegistryIssue`s during the scan. "Удалить запись" (`FixSelectedAsync`) confirms, then deletes the key for real (`DeleteSubKeyTree`; HKLM needs admin → reverts + dialog). DashReg and the registry badge are real counts. Re-scanning reflects reality (deleted entries don't reappear).

**Disk** is fully real: total/used/free + used% come from `DriveInfo` of the system drive (`DiskTotalText`/`DiskUsedText`/`DiskFreeText`/`UsedPercent`). "Крупные папки" are real special-folder paths — each row is a button that opens the folder in Explorer (`OpenFolderCommand`), and sizes are measured lazily on first Disk visit (`ComputeFolderSizesAsync`, progressive). The **type breakdown is real too** (`DiskTypeScanner`): on first Disk visit a one-time background walk of the system drive (`StartDiskTypeScan`, throttled `IProgress<DiskTypeSnapshot>`) groups every file's bytes by extension into categories (video/photo/audio/documents/archives/apps/other), skipping reparse points + access-denied. `RebuildDiskBreakdown` reconciles the typed total against `DriveInfo` used space (unaccounted protected/system bytes become a "Система" bucket, free space a "Свободно" bucket) and fills two views: the Dashboard `DiskSegments` (% of total disk, top 6) and the Disk-page `DiskTiles` rendered as a **squarified treemap** via `Helpers/TreemapPanel` (attached `Weight` ∝ bytes). Updates live as the walk proceeds (`DiskScanStatus`, `DiskAnalyzing`/`DiskAnalyzed`/`DiskNotAnalyzed`); category names/colors are Loc keys (`disktype.*`) rebuilt on language change.

**Drivers** (`DriverService`) is real: on first visit to the Drivers page (`ScanDriversAsync`) it enumerates installed drivers from WMI `Win32_PnPSignedDriver` (real device name/publisher/version/date/class), deduped by name and filtered to meaningful device classes. "Outdated" is a conservative heuristic — a **non-Microsoft** driver on an updatable class (DISPLAY/NET/MEDIA/BLUETOOTH/IMAGE) whose `DriverDate` is >18 months old (Microsoft inbox drivers carry old dates by design, so they're excluded). We never download/replace driver binaries ourselves; "Обновить"/"Обновить все" (`UpdateDriverCommand`/`UpdateAllDriversCommand`) open **Windows Update** (`ms-settings:windowsupdate`) — the safe real path. The nav badge = outdated count; the scan shows real progress (`DriverScanProgress`/`DriverScanDevice`). `DriverInfo` resolves localized type/status labels via `Loc` (`drv.*`). Needs the `System.Management` package.

Real: scanning (`ScannerService`, incl. cookies), cleaning (`CleanerService`), Startup (`StartupService`), Apps (`AppsService`), Registry (`RegistryService`), Disk figures + folders + **type breakdown** (`DriveInfo` + `DiskTypeScanner`), Drivers (`DriverService` via WMI). The Dashboard STAT cards are real too (JUNK from the scan, REGISTRY count, STARTUP = `BootSec` heuristic, FREE = `DiskFreeText`). Settings and Scheduler are functional (persisted + real autostart/task). **Nothing in the app is mock anymore** — every page is wired to real Windows data.

## Localization (i18n)

The UI is bilingual (**English default**, Russian) via `Helpers/Loc.cs` — a singleton with a `this[key]` indexer and two dictionaries. **English is the launch default.** The Settings page switches language live (`ChangeLanguageCommand` → `Loc.SetLanguage`).

- **XAML:** bind static strings as `Text="{Binding [some.key], Source={StaticResource Loc}}"` (`Loc` is an app resource registered in `App.xaml`). Don't hardcode user-facing text. **Gotcha:** on a `<Run Text="…">` you must add `Mode=OneWay` — `Run.Text` binds two-way by default and the `Loc` indexer is read-only, so without it the app throws at load.
- **Code:** use `Loc.Instance["key"]` (the VM exposes `private static Loc L => Loc.Instance;`). All VM strings (`PageTitle`, dialogs, composite "Clean {0}"/"{0} selected" via `CleanButtonText`/`SelectedText`, stats formats) go through `L[...]`.
- **Persistence:** preferences (language, notifications, autostart, scheduler) are saved to `%AppData%\SwiftClean\settings.json` via `SettingsStore`, loaded in the `MainViewModel` ctor. "Start with Windows" is real — `AutostartManager` writes/removes the `HKCU\…\Run\SwiftClean` value (its registry presence is the source of truth, not the JSON).
- **Scheduler is real:** "Save schedule" (`SaveScheduleCommand`) calls `SchedulerService` which creates/removes a Windows Task Scheduler task ("SwiftClean Auto-Clean") via the `schtasks` CLI (per-user, no admin) on the chosen frequency, **at the chosen run time** (`ScheduleTimeText`, stepped ±30 min via `TimeUp/TimeDownCommand`, persisted as `SchedulerTime`). The Scheduler page's "what to clean" checkboxes are **real and persisted** (`SchedCleanTemp/Recycle/Cache` → settings); they're two-way `BoxCheck` toggles, not static. The task runs `SwiftClean.exe --autoclean`, which `App.OnStartup` detects and handles **headlessly** (no window): it reads the saved selection and scans+recycles only those categories (mapped to "Temp Files"/"Recycle Bin"/"Browser Cache"), then `Shutdown()`. So `App.xaml` has **no `StartupUri`** — `OnStartup` creates the `MainWindow` for the normal path and branches on `--autoclean`.
- **Switching:** `Loc.SetLanguage` raises the indexer change (refreshes every XAML binding) and fires `LanguageChanged`. `MainViewModel.OnLanguageChanged` then `OnPropertyChanged("")` (refresh all computed VM strings), `NavigationView.Refresh()` (re-group the localized sidebar sections), and rebuilds the scanned lists. Data models that carry localized text (`NavigationItem`, `StartupApp`, `DiskFolder`, `ScanStage`, `CleanItem`, `RegistryIssue`, `DriverInfo`) resolve via `Loc` keys; the long-lived ones subscribe to `LanguageChanged`, the per-scan ones (`CleanItem`/`RegistryIssue`) are rebuilt.
- **Adding a string:** add the key to **both** `En` and `Ru` dictionaries in `Loc.cs`, then reference it. Format strings use `{0}`/`{1}` and `string.Format(L["key"], …)`.

**Scroll note:** the content host is one outer `ScrollViewer` (`ContentScroller`). Pages with their *own* inner `ScrollViewer` (Dashboard, Registry, Apps) must be capped with `MaxHeight="{Binding ViewportHeight, ElementName=ContentScroller}"` in `MainWindow.xaml` — otherwise the inner viewer gets infinite height, never scrolls, and swallows the mouse wheel. Tall form pages (Cleaning/Startup/Disk/Scheduler/Settings) have no inner scroll and rely on the outer one, so they must NOT be capped. Replacing a mock = add a `Service`, populate the matching collection on `MainViewModel`, keep the same models/styles.
