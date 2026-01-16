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

### Versioning & Releases (Nerdbank.GitVersioning)

This repo uses Nerdbank.GitVersioning (NBGV) for SemVer and RC builds.

- Base version is defined in `version.json` (e.g., `0.2.0`).
- RC builds are produced from tags like `v0.2.0-rc.1`, `v0.2.0-rc.2`, etc.
- Stable releases are produced from tags like `v0.2.0`.

Typical flow:

1. Tag an RC: `git tag v0.2.0-rc.1 && git push origin v0.2.0-rc.1`
2. Build/package and publish RC to NuGet.
3. After validation, tag stable: `git tag v0.2.0 && git push origin v0.2.0`
4. Build/package and publish stable.

## Roadmap / Future Enhancements

- Attribute-based command & option metadata
- Sub-commands via grouped classes
- Validation (DataAnnotations)
- Source generator for AOT friendliness
- Middleware / pipeline hooks

## License

MIT
