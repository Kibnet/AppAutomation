# AppAutomation Next Steps

1. Replace placeholder values in `tests/<YourApp>.AppAutomation.TestHost/<YourApp>AppLaunchHost.cs`.
2. Add stable `AutomationId` values in your Avalonia app.
3. Replace `AvaloniaAppType` in `TestHost`; the generated `HeadlessSessionHooks` already use it.
4. Run `dotnet tool run appautomation doctor --repo-root . --strict` from the repository root.
5. Start with `Headless`, then enable `FlaUI`.
