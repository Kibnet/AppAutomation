using System.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

namespace AppAutomation.Tooling.Tests;

public sealed class DoctorPlaceholderTests
{
    [Test]
    public async Task Doctor_NonStrict_Warns_OnGeneratedPlaceholderScaffold()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteConsumerFixture(workspace.FullPath, includePlaceholders: true);

        var result = InvokeDoctor(workspace.FullPath, strict: false);

        using (Assert.Multiple())
        {
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput).Contains("Generated scaffold still contains placeholder markers");
            await Assert.That(result.StandardOutput).Contains("REPLACE_WITH_YOUR_");
        }
    }

    [Test]
    public async Task Doctor_Strict_Fails_OnGeneratedPlaceholderScaffold()
    {
        using var workspace = TemporaryWorkspace.Create();
        WriteConsumerFixture(workspace.FullPath, includePlaceholders: true);

        var result = InvokeDoctor(workspace.FullPath, strict: true);

        using (Assert.Multiple())
        {
            await Assert.That(result.ExitCode == 0).IsEqualTo(false);
            await Assert.That(result.StandardOutput).Contains("Generated scaffold still contains placeholder markers");
            await Assert.That(result.StandardOutput).Contains("HeadlessSessionHooks");
        }
    }

    private static ScriptResult InvokeDoctor(string repositoryRoot, bool strict)
    {
        var repoRoot = GetRepoRoot();
        var toolingAssemblyPath = GetToolingAssemblyPath(repoRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(toolingAssemblyPath);
        startInfo.ArgumentList.Add("doctor");
        startInfo.ArgumentList.Add("--repo-root");
        startInfo.ArgumentList.Add(repositoryRoot);
        if (strict)
        {
            startInfo.ArgumentList.Add("--strict");
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start AppAutomation.Tooling.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
    }

    private static string GetToolingAssemblyPath(string repoRoot)
    {
        var toolingAssemblyPath = Path.Combine(
            repoRoot,
            "src",
            "AppAutomation.Tooling",
            "bin",
            GetBuildConfiguration(),
            "net8.0",
            "AppAutomation.Tooling.dll");

        if (File.Exists(toolingAssemblyPath))
        {
            return toolingAssemblyPath;
        }

        throw new FileNotFoundException(
            $"Built AppAutomation.Tooling assembly was not found. Expected path: {toolingAssemblyPath}",
            toolingAssemblyPath);
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static void WriteConsumerFixture(string repositoryRoot, bool includePlaceholders)
    {
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "global.json"), """
{
  "sdk": {
    "version": "10.0.100"
  }
}
""");
        File.WriteAllText(Path.Combine(repositoryRoot, "NuGet.Config"), """
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
""");

        WriteProject(
            repositoryRoot,
            "tests\\Sample.UiTests.Authoring\\Sample.UiTests.Authoring.csproj",
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AppAutomation.Abstractions" Version="*" />
  </ItemGroup>
</Project>
""");
        WriteProject(
            repositoryRoot,
            "tests\\Sample.UiTests.Headless\\Sample.UiTests.Headless.csproj",
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AppAutomation.Avalonia.Headless" Version="*" />
  </ItemGroup>
</Project>
""");
        WriteProject(
            repositoryRoot,
            "tests\\Sample.UiTests.FlaUI\\Sample.UiTests.FlaUI.csproj",
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows7.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AppAutomation.FlaUI" Version="*" />
  </ItemGroup>
</Project>
""");
        WriteProject(
            repositoryRoot,
            "tests\\Sample.AppAutomation.TestHost\\Sample.AppAutomation.TestHost.csproj",
            """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AppAutomation.TestHost.Avalonia" Version="*" />
  </ItemGroup>
</Project>
""");

        if (!includePlaceholders)
        {
            return;
        }

        WriteFile(
            repositoryRoot,
            "tests\\Sample.UiTests.Headless\\Infrastructure\\HeadlessSessionHooks.cs",
            """
using TUnit.Core;

namespace Sample.UiTests.Headless.Infrastructure;

public static class HeadlessSessionHooks
{
    [Before(TestSession)]
    public static void SetupSession()
    {
        // TODO: Start your Avalonia Headless session and register it via HeadlessRuntime.SetSession(...).
    }
}
""");
        WriteFile(
            repositoryRoot,
            "tests\\Sample.AppAutomation.TestHost\\SampleAppLaunchHost.cs",
            """
namespace Sample.AppAutomation.TestHost;

public static class SampleAppLaunchHost
{
    public const string DesktopExecutable = "REPLACE_WITH_YOUR_DESKTOP_EXE.exe";
}
""");
    }

    private static void WriteProject(string repositoryRoot, string relativePath, string contents)
    {
        WriteFile(repositoryRoot, relativePath, contents);
    }

    private static void WriteFile(string repositoryRoot, string relativePath, string contents)
    {
        var fullPath = Path.Combine(repositoryRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    private static string GetRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AppAutomation.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AppAutomation.sln.");
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string fullPath)
        {
            FullPath = fullPath;
        }

        public string FullPath { get; }

        public static TemporaryWorkspace Create()
        {
            var fullPath = Path.Combine(
                Path.GetTempPath(),
                "AppAutomationToolingTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fullPath);
            return new TemporaryWorkspace(fullPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
    }
}
