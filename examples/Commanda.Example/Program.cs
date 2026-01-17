using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Commanda;
using Commanda.Example;

var builder = Host.CreateApplicationBuilder(args);

// Register services
builder.Services.AddSingleton<GreetingService>();

// Add simple commands
builder.AddCommand("greet", "Say hello", (string name) =>
{
    Console.WriteLine($"Hello, {name}!");
});

builder.AddCommand("sum", (int a, int b) =>
{
    Console.WriteLine(a + b);
});

builder.AddCommand("hello", (GreetingService svc) => GreetingService.SayHello());
builder.AddCommand("hello-async", async (GreetingService svc, string name) => await GreetingService.SayHelloAsync(name));

// Example with Option attributes: alias inference and bool flag
builder.AddCommand("hello-opt", async (
    GreetingService svc,
    [Option(Description = "Name to greet")] string name,
    [Option("excited", Description = "Add exclamation (default false)")] bool excited = false
) =>
{
    await GreetingService.SayHelloAsync(name, excited);
});

var host = builder.Build();
return await host.RunCommandsAsync(args);
