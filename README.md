# Commanda

A lightweight, Cocona-like command line builder for .NET, combining `System.CommandLine` with the Generic Host (`HostBuilder`).

Supported target frameworks: `net8.0`, `net9.0`, `net10.0`.

## Features

- Fluent registration via `IHostApplicationBuilder`
- Delegate-based command handlers (sync or async)
- DI parameter injection for reference type parameters
- Automatic binding of primitive/string parameters from CLI
- Support for default help output from System.CommandLine
- Attribute-based options via `[Option]` (alias inference, descriptions, bool flags)

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

## Attribute-Based Options

Use `[Option]` on parameters to turn them into named options with help descriptions. Parameters without `[Option]` remain positional arguments.

Rules:

- Alias: If `Name` is not specified, the long alias is inferred from the parameter name in kebab-case (e.g., `containerName` → `--container-name`).
- Description: Use `Description` to populate help text.
- Required vs optional: Parameters without a default are required; parameters with a default become optional and use the default when omitted.
- Bool flags: `[Option] bool` acts as a switch; presence sets `true`. You can also pass `--flag false` to override. Default is `false` unless a parameter default is provided.

Example:

```csharp
builder.AddCommand("hello-opt", async (
    GreetingService svc,
    [Option(Description = "Name to greet")] string name,
    [Option("excited", Description = "Add exclamation (default false)")] bool excited = false
) =>
{
    await svc.SayHelloAsync(name);
    if (excited) Console.WriteLine("!");
});
```

Try it:

```bash
dotnet run --project examples/Commanda.Example -- hello-opt --name Alice
dotnet run --project examples/Commanda.Example -- hello-opt --name Bob --excited
```

## Roadmap / Future Enhancements

- Attribute-based command & option metadata
- Sub-commands via grouped classes
- Validation (DataAnnotations)
- Source generator for AOT friendliness
- Middleware / pipeline hooks

## License

MIT
