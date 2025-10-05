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

builder.AddCommand("hello", (GreetingService svc) => svc.SayHello());
builder.AddCommand("hello-async", async (GreetingService svc, string name) => await svc.SayHelloAsync(name));

var host = builder.Build();
return await host.RunCommandsAsync(args);
