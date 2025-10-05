namespace Commanda;

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq.Expressions;

public static class CommandHostBuilderExtensions
{
    private const string RegistryKey = "__CommandaRegistry";

    private static CommandRegistry GetOrCreateRegistry(IHostApplicationBuilder builder)
    {
        if (builder.Properties.TryGetValue(RegistryKey, out var existing) && existing is CommandRegistry reg)
            return reg;
        var registry = new CommandRegistry();
        builder.Properties[RegistryKey] = registry;
        // Also register in DI so the built host can retrieve it later.
        builder.Services.AddSingleton(registry);
        return registry;
    }

    public static IHostApplicationBuilder AddCommand(this IHostApplicationBuilder builder, string name, Delegate handler)
        => builder.AddCommand(name, description: null, handler);

    public static IHostApplicationBuilder AddCommand(this IHostApplicationBuilder builder, string name, string? description, Delegate handler)
    {
        var registry = GetOrCreateRegistry(builder);
        registry.Add(new CommandDescriptor
        {
            Name = name,
            Description = description,
            Handler = handler,
            Parameters = handler.Method.GetParameters()
        });
        return builder;
    }

    // Simple scanner: public static methods returning void/Task on type T decorated with no attribute for now.
    public static IHostApplicationBuilder AddCommands<T>(this IHostApplicationBuilder builder)
    {
        var type = typeof(T);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && (m.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(m.ReturnType)));

        foreach (var m in methods)
        {
            // We'll wrap invocation manually rather than constructing exact delegate type.
            Delegate del = (Func<IServiceProvider, object?[], Task>)(async (sp, providedArgs) =>
            {
                object? target = null;
                if (!m.IsStatic)
                {
                    target = sp.GetRequiredService(type);
                }
                var result = m.Invoke(target, providedArgs);
                if (result is Task t)
                {
                    await t.ConfigureAwait(false);
                }
            });

            // Store a descriptor with synthetic parameters list from method.
            var descriptor = new CommandDescriptor
            {
                Name = m.Name.ToLowerInvariant(),
                Description = m.Name,
                Handler = del,
                Parameters = m.GetParameters()
            };
            var registry = GetOrCreateRegistry(builder);
            registry.Add(descriptor);
            // Ensure the declaring type can be resolved if instance methods exist.
            if (!m.IsStatic)
            {
                builder.Services.AddTransient(type);
            }
        }
        return builder;
    }

    public static async Task<int> RunCommandsAsync(this IHost host, string[] args)
    {
        var registry = host.Services.GetService<CommandRegistry>();
        if (registry == null)
        {
            Console.Error.WriteLine("No commands registered.");
            return 1;
        }

        if (args.Length == 0)
        {
            PrintHelp(registry);
            return 0;
        }

        var cmdName = args[0];
        var descriptor = registry.Descriptors.FirstOrDefault(d => string.Equals(d.Name, cmdName, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null)
        {
            Console.Error.WriteLine($"Unknown command '{cmdName}'.\n");
            PrintHelp(registry);
            return 1;
        }

        var parameters = descriptor.Parameters;
        var provided = args.Skip(1).ToArray();
        var finalArgs = new object?[parameters.Length];
        int valueTokenIndex = 0;
        var sp = host.Services;

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            object? value = null;
            if (p.ParameterType != typeof(string) && !p.ParameterType.IsValueType)
            {
                value = sp.GetService(p.ParameterType);
            }
            else
            {
                if (valueTokenIndex < provided.Length)
                {
                    try
                    {
                        value = Convert.ChangeType(provided[valueTokenIndex], p.ParameterType);
                        valueTokenIndex++;
                    }
                    catch
                    {
                        value = p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
                    }
                }
                else if (p.HasDefaultValue)
                {
                    value = p.DefaultValue;
                }
                else
                {
                    Console.Error.WriteLine($"Missing required argument '{p.Name}'.");
                    return 1;
                }
            }
            finalArgs[i] = value;
        }

        var result = descriptor.Handler.DynamicInvoke(finalArgs);
        if (result is Task task) await task.ConfigureAwait(false);
        return 0;
    }

    private static void PrintHelp(CommandRegistry registry)
    {
        Console.WriteLine("Available commands:");
        foreach (var d in registry.Descriptors.OrderBy(d => d.Name))
        {
            var sig = string.Join(" ", d.Parameters.Select(p => p.Name));
            Console.WriteLine($"  {d.Name} {sig}    {d.Description}");
        }
    }
}

// Removed static fallback; DI is the single source of truth.
