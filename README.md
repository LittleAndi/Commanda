# Commanda

A lightweight, Cocona-like command line builder for .NET, combining `System.CommandLine` with the Generic Host (`HostBuilder`).

Supported target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Features

- Fluent registration via `IHostApplicationBuilder`
- Delegate-based command handlers (sync or async)
- DI parameter injection for reference type parameters
- Automatic binding of primitive/string parameters from CLI
- Support for default help output from System.CommandLine

## Quick Start

```csharp
using Microsoft.Extensions.Hosting;
using Commanda;

var builder = Host.CreateApplicationBuilder(args);

builder.AddCommand("greet", "Say hello", (string name) =>
{
    Console.WriteLine($"Hello, {name}!");
});

builder.AddCommand("sum", (int a, int b) =>
{
    Console.WriteLine(a + b);
});

var app = builder.Build();
await app.RunCommandsAsync(args);
```

Run:

```bash
dotnet run --project examples/Commanda.Example -- greet Alice
dotnet run --project examples/Commanda.Example -- sum 2 5
```

## Dependency Injection

Any non-primitive, non-string parameter will be resolved from the host service provider.

```csharp
builder.Services.AddSingleton<GreetingService>();

builder.AddCommand("hello", (GreetingService svc) => svc.SayHello());
```

## Installation (Future NuGet)

Planned package id: `Commanda`.

### Versioning & Releases (nbgv CLI)

We use the `nbgv` CLI and publish on tags. No separate release branches are required.

Install:

```bash
dotnet tool install -g nbgv
```

Set the next development version on main:

```bash
# Example: bump to 0.3.0 pre-release
nbgv set-version 0.3.0-alpha
git commit -am "Set version to 0.3.0-alpha"
git push
```

Create and publish a Release Candidate (on main or a feature branch you plan to release):

```bash
# Tag the RC and push the tag
nbgv tag 0.3.0-rc.1
git push origin v0.3.0-rc.1
```

Publish a stable release:

```bash
# Remove pre-release, set the final version, tag, and push
nbgv set-version 0.3.0
git commit -am "Set version to 0.3.0"
nbgv tag 0.3.0
git push origin v0.3.0
```

CI publishes to nuget.org on tags matching `v*` using Trusted Publishing (OIDC). Ensure nuget.org Trusted Publishing is configured for this repo/workflow.

Tips:

- Do not move tags after publishing to NuGet; use a new tag (e.g., `-rc.2`) instead.
- Make sure your tags use the `v` prefix; if so, set `"tagPrefix": "v"` in `version.json`.

## Roadmap / Future Enhancements

- Attribute-based command & option metadata
- Sub-commands via grouped classes
- Validation (DataAnnotations)
- Source generator for AOT friendliness
- Middleware / pipeline hooks

## License

MIT
