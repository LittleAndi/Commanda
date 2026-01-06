# Commanda

A lightweight, Cocona-like command line builder for .NET 10, combining `System.CommandLine` with the Generic Host (`HostBuilder`).

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

## Roadmap / Future Enhancements

- Attribute-based command & option metadata
- Sub-commands via grouped classes
- Validation (DataAnnotations)
- Source generator for AOT friendliness
- Middleware / pipeline hooks

## License

MIT
