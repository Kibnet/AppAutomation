# AppAutomation Next Steps

1. Replace placeholder values in `tests/<YourApp>.AppAutomation.TestHost/<YourApp>AppLaunchHost.cs`.
2. Add stable `AutomationId` values in your Avalonia app.
3. Replace `AvaloniaAppType` in `TestHost`; the generated `RenderedHeadlessAppBuilder` and `HeadlessSessionHooks` use it to enable real headless rendering.
4. Run `dotnet tool run appautomation doctor --repo-root . --strict` from the repository root.
5. Start with `Headless`, then enable `FlaUI`.
6. Run `CaptureScreenshotExample` in the generated Headless project, then open the PNG path printed by the test. Failure screenshots are attached automatically for tests that derive from the generated headless class. See [Headless screenshots](https://github.com/Kibnet/AppAutomation/blob/main/docs/appautomation/headless-screenshots.md).
