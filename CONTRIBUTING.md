# Contributing to AppAutomation

Thank you for your interest in contributing to AppAutomation! This guide will help you set up your development environment and understand our contribution workflow.

## Prerequisites

### .NET SDK

This project requires **.NET SDK 10.0.202**. The SDK version is pinned in `global.json` to ensure consistent builds across all contributors.

To install the required SDK, run the following command from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File ./eng/install-dotnet.ps1
```

This script downloads and installs the pinned SDK version into the `.dotnet` directory within the repository. The `global.json` configuration automatically resolves the local SDK when building.

## Building

To build the entire solution:

```bash
dotnet build AppAutomation.sln -c Release
```

For debug builds:

```bash
dotnet build AppAutomation.sln -c Debug
```

## Running Tests

To run all tests:

```bash
dotnet test AppAutomation.sln -c Release
```

The project uses TUnit as the test framework with Microsoft.Testing.Platform as the test runner (configured in `global.json`).

## Packaging

To create NuGet packages, use the `eng/pack.ps1` script:

```powershell
./eng/pack.ps1
```

### Parameters

| Parameter       | Description                                           | Default       |
|-----------------|-------------------------------------------------------|---------------|
| `-Configuration`| Build configuration                                   | `Release`     |
| `-OutputRoot`   | Output directory for packages                         | `artifacts/packages/<version>` |
| `-Version`      | Package version (resolved from `eng/Versions.props` if not specified) | Auto-resolved |
| `-NoBuild`      | Skip building before packing                          | `false`       |

### Examples

Pack with default settings:
```powershell
./eng/pack.ps1
```

Pack a specific version:
```powershell
./eng/pack.ps1 -Version "2.1.0"
```

Pack without rebuilding (if already built):
```powershell
./eng/pack.ps1 -NoBuild
```

## Smoke Testing

The `eng/smoke-consumer.ps1` script validates that the produced NuGet packages work correctly by creating a temporary consumer project that references the packages, builds it, and runs the `appautomation doctor` tool.

```powershell
./eng/smoke-consumer.ps1
```

### Parameters

| Parameter         | Description                                           | Default       |
|-------------------|-------------------------------------------------------|---------------|
| `-Configuration`  | Build configuration                                   | `Release`     |
| `-PackagesPath`   | Path to pre-built packages                            | Auto-resolved |
| `-Version`        | Package version to test                               | Auto-resolved |
| `-WorkspaceRoot`  | Custom workspace directory for smoke tests            | Temp directory |
| `-SkipPack`       | Skip running pack.ps1 (requires pre-built packages)   | `false`       |
| `-KeepWorkspace`  | Preserve the temporary workspace after completion     | `false`       |

### What It Does

1. Packs the solution if packages don't exist (unless `-SkipPack` is specified)
2. Creates a temporary consumer solution with authoring and runtime test projects
3. References AppAutomation packages from the local package source
4. Builds and validates the consumer projects
5. Scaffolds a project using the `AppAutomation.Templates` template
6. Runs `appautomation doctor` to validate the generated project structure
7. Cleans up the temporary workspace (unless `-KeepWorkspace` is specified)

## Project Structure

| Directory   | Description                                                                 |
|-------------|-----------------------------------------------------------------------------|
| `src/`      | Framework source code (core abstractions, authoring, runtime adapters)     |
| `sample/`   | Reference implementation and sample applications demonstrating usage       |
| `tests/`    | Framework unit and integration tests                                        |
| `eng/`      | Build, packaging, and CI/CD scripts                                         |
| `docs/`     | Documentation files                                                         |
| `specs/`    | Design specifications                                                       |
| `artifacts/`| Build outputs and packaged NuGet packages (generated)                      |

### Key Source Projects

- **AppAutomation.Abstractions** — Core interfaces and contracts
- **AppAutomation.Authoring** — Roslyn source generator for page object models
- **AppAutomation.TUnit** — TUnit integration and test base classes
- **AppAutomation.FlaUI** — FlaUI runtime adapter for Windows UI Automation
- **AppAutomation.Avalonia.Headless** — Avalonia headless testing adapter
- **AppAutomation.TestHost.Avalonia** — Test host for Avalonia applications
- **AppAutomation.Templates** — Project templates for new consumers
- **AppAutomation.Tooling** — CLI tools including `appautomation doctor`

## Pull Request Guidelines

Before submitting a pull request, please ensure:

1. **Build passes** — Run `dotnet build AppAutomation.sln -c Release` and verify no errors
2. **Tests pass** — Run `dotnet test AppAutomation.sln -c Release` and verify all tests succeed
3. **Code style** — Follow the existing code style defined in `.editorconfig`:
   - Use 4-space indentation
   - UTF-8 encoding with CRLF line endings
   - Include a final newline in files
   - Sort `System` using directives first
   - Avoid `this.` qualification for members
   - Prefer explicit types over `var`
   - Always use braces for control statements

4. **Smoke test** — For changes affecting packaging or templates, run `./eng/smoke-consumer.ps1`

### Code Review Process

- All pull requests require at least one approving review
- CI validation must pass before merging
- Keep commits focused and atomic
- Write clear commit messages describing the change
