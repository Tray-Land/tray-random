@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app built from the `winui-tray-app` skill scaffold on top of `winapp new`. Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout (`Views/TrayFlyoutWindow`) is hidden on dismiss and closed after a minute hidden. Anything it starts, it must stop in `FlyoutPage.OnHidden` / `Dispose`.
- The app makes no network calls (no `internetClient` capability). Everything is generated locally by Bogus; nothing runs while the flyout is hidden.
- `TrayRandom.Core` (net10.0, no WinRT) holds the category catalog, dice notation, and favorites logic; `TrayRandom.Tests` covers it with MSTest (`dotnet test`). Category ids are persisted in favorites: add or remove categories, never rename an id.
- Settings are a page inside the flyout (`Views/SettingsPage`), not a window.
- The repo is MIT licensed (`LICENSE`). When the app's features are working, write a descriptive `README.md` (what it does, screenshot, install, setup/keys, privacy, build from source, license); the `winui-tray-app` skill lists the sections.
- Put pure decision logic (parsing, policies, formatting) in classes with no WinRT dependency, so a plain `net10.0` test project can cover it.
