### 📋 GitHub Copilot Build Instructions — Commanda

**Goal:**
Build a lightweight, modern .NET library named **Commanda** — a Cocona-style wrapper for `System.CommandLine` that works seamlessly with the `HostBuilder` API.

---

### 🏗️ Project setup

1. Create a new solution:

   ```bash
   dotnet new sln -n Commanda
   mkdir src tests examples
   ```

2. Create the core library:

   ```bash
   dotnet new classlib -n Commanda -o src/Commanda
   dotnet sln add src/Commanda/Commanda.csproj
   ```

3. Create an example console app:

   ```bash
   dotnet new console -n Commanda.Example -o examples/Commanda.Example
   dotnet add examples/Commanda.Example reference src/Commanda/Commanda.csproj
   dotnet sln add examples/Commanda.Example/Commanda.Example.csproj
   ```

---

### 🧩 Library requirements

> Use modern C# 12 / .NET 8 (now targeting .NET 9 in the repo) features and idiomatic APIs.
>
> IMPORTANT: `System.CommandLine` is still prerelease. When adding it manually use:
>
> ```bash
> dotnet add package System.CommandLine --prerelease
> ```

- Namespace: `Commanda`
- Provide these extension methods:

  - `AddCommand(string name, string? description, Delegate handler)`
  - `AddCommands<T>()` (optional class scanner)
  - `RunCommandsAsync(string[] args)`

- Built on **`System.CommandLine`** and **`Microsoft.Extensions.Hosting`**
- Support:

  - DI parameter injection
  - Async commands
  - Default help output

- No reflection magic at runtime (beyond minimal parameter binding)

---

### 🪄 Example usage

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
    Console.WriteLine($"{a + b}");
});

var app = builder.Build();
await app.RunCommandsAsync(args);
```

---

### ⚙️ Implementation hints for Copilot

1. Create a class `CommandRegistry` that holds registered commands.
2. Create extension methods in `CommandHostBuilderExtensions` for:

   - `AddCommand()` → registers commands to a shared `CommandRegistry`
   - `RunCommandsAsync()` → builds a `RootCommand` and runs `InvokeAsync()`

3. Bind parameters using reflection to generate `Option` or `Argument` instances.
4. Use DI (`IServiceProvider`) to resolve handler targets.
5. Make the library minimal and dependency-free (only Microsoft.Extensions + System.CommandLine). Because System.CommandLine is prerelease, pin an explicit prerelease version or use the `--prerelease` flag when adding.

---

### 🧪 Example project

Under `/examples/Commanda.Example`, show:

- Simple commands (`greet`, `sum`)
- Example with DI:

  ```csharp
  builder.Services.AddSingleton<GreetingService>();
  builder.AddCommand("hello", (GreetingService svc) => svc.SayHello());
  ```

---

### 📦 NuGet packaging

Add this to `src/Commanda/Commanda.csproj`:

```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  <PackageId>Commanda</PackageId>
  <Version>0.1.0</Version>
  <Authors>Lars Nyström</Authors>
  <Description>Cocona-like CLI builder for .NET using HostBuilder and System.CommandLine.</Description>
  <RepositoryUrl>https://github.com/<yourusername>/Commanda</RepositoryUrl>
  <PackageTags>cli commandline hostbuilder cocona system.commandline</PackageTags>
</PropertyGroup>
```

### 🧭 Future enhancements

- `[Command]` and `[Option]` attributes
- Validation via `DataAnnotations`
- Subcommands (grouped via classes)
- Source generator for compile-time registration (AOT-ready)
- Middleware system for logging, telemetry, etc.

---

### 🧠 Copilot guidance

> You are building a lightweight open-source .NET library named **Commanda**.
> The purpose is to bring Cocona-like syntax to apps built with the .NET Generic Host and System.CommandLine.
> Use extension methods, clean naming, async-first patterns, and DI integration.
> Target .NET 8 and ensure zero dependencies outside `System.CommandLine` and `Microsoft.Extensions.*`.
