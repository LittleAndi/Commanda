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
        if (registry == null || registry.Descriptors.Count == 0)
        {
            Console.Error.WriteLine("No commands registered.");
            return 1;
        }

        var sp = host.Services;

        // Store binding configuration per command
        var bindingMap = new Dictionary<string, (ParameterInfo[] parameters, List<(int index, Type type, bool isCli, bool isOption, object symbol, string? alias)> bindings, List<(int index, Type type)> diParams)>();

        foreach (var descriptor in registry.Descriptors)
        {
            var parameters = descriptor.Parameters;
            // Track how to bind each parameter at invocation time.
            var bindings = new List<(int index, Type type, bool isCli, bool isOption, object symbol, string? alias)>();
            var diParams = new List<(int index, Type type)>();

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pType = p.ParameterType;

                // DI-only for non-string reference types.
                if (pType != typeof(string) && !pType.IsValueType)
                {
                    diParams.Add((i, pType));
                    continue;
                }

                var optAttr = p.GetCustomAttribute<OptionAttribute>();
                if (optAttr != null)
                {
                    var aliasLong = "--" + (string.IsNullOrWhiteSpace(optAttr.Name) ? ToKebabCase(p.Name!) : optAttr.Name);
                    // We only need alias and type for manual parsing
                    bindings.Add((i, pType, true, true, aliasLong /*symbol unused*/, aliasLong));
                }
                else
                {
                    // Positional argument: keep index and type only
                    bindings.Add((i, pType, true, false, p.Name!, null));
                }
            }

            // Cache binding info for invocation
            bindingMap[descriptor.Name] = (parameters, bindings, diParams);

            // No System.CommandLine runtime wiring; manual dispatch below.
        }

        // If no args, print help summary
        if (args.Length == 0)
        {
            PrintHelp(registry);
            return 0;
        }

        // Manual dispatch to selected command name
        var invokedName = args[0];
        if (string.IsNullOrEmpty(invokedName) || !bindingMap.TryGetValue(invokedName!, out var bindInfo))
        {
            Console.Error.WriteLine("Unknown or missing command.\n");
            PrintHelp(registry);
            return 1;
        }

        var (paramInfos, paramBindings, paramDiParams) = bindInfo;
        var finalArgs = new object?[paramInfos.Length];

        // Manual parse of tokens: options ("--alias" [value]) and positional arguments
        var tokens = args.Skip(1).ToList();
        var optMap = paramBindings.Where(b => b.isOption && b.alias != null)
            .ToDictionary(b => b.alias!, b => b);
        var argQueue = new Queue<(int index, Type type)>(paramBindings.Where(b => !b.isOption).Select(b => (b.index, b.type)));

        for (int ti = 0; ti < tokens.Count; ti++)
        {
            var tok = tokens[ti];
            if (tok.StartsWith("--"))
            {
                if (!optMap.TryGetValue(tok, out var b)) continue; // unknown options ignored
                if (b.type == typeof(bool))
                {
                    bool val = true;
                    if (ti + 1 < tokens.Count && !tokens[ti + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        if (bool.TryParse(tokens[ti + 1], out var parsed))
                        {
                            val = parsed;
                            ti++;
                        }
                    }
                    finalArgs[b.index] = val;
                }
                else
                {
                    if (ti + 1 < tokens.Count && !tokens[ti + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        try
                        {
                            var converted = Convert.ChangeType(tokens[ti + 1], b.type);
                            finalArgs[b.index] = converted;
                            ti++;
                        }
                        catch
                        {
                            // leave unset; will be validated later
                        }
                    }
                }
            }
            else if (argQueue.Count > 0)
            {
                var (idx, ptype) = argQueue.Dequeue();
                try
                {
                    finalArgs[idx] = Convert.ChangeType(tok, ptype);
                }
                catch
                {
                    // leave unset; will be validated later
                }
            }
        }

        // Apply defaults and validate requireds
        for (int i = 0; i < paramInfos.Length; i++)
        {
            if (finalArgs[i] != null) continue;
            var p = paramInfos[i];
            if (p.ParameterType != typeof(string) && !p.ParameterType.IsValueType)
            {
                // DI param handled later
                continue;
            }
            var optAttr = p.GetCustomAttribute<OptionAttribute>();
            if (optAttr != null)
            {
                if (p.ParameterType == typeof(bool))
                {
                    finalArgs[i] = p.HasDefaultValue ? p.DefaultValue : false;
                }
                else if (p.HasDefaultValue)
                {
                    finalArgs[i] = p.DefaultValue;
                }
                else
                {
                    Console.Error.WriteLine($"Missing required option '--{(string.IsNullOrWhiteSpace(optAttr.Name) ? ToKebabCase(p.Name!) : optAttr.Name)}'.");
                    return 1;
                }
            }
            else
            {
                if (p.HasDefaultValue)
                {
                    finalArgs[i] = p.DefaultValue;
                }
                else
                {
                    Console.Error.WriteLine($"Missing required argument '{p.Name}'.");
                    return 1;
                }
            }
        }

        foreach (var (index, type) in paramDiParams)
        {
            finalArgs[index] = sp.GetService(type);
        }

        var selectedDescriptor = registry.Descriptors.First(d => d.Name == invokedName);
        var handlerParams = selectedDescriptor.Handler.Method.GetParameters();
        object? invokeResult;
        if (handlerParams.Length == paramInfos.Length)
        {
            invokeResult = selectedDescriptor.Handler.DynamicInvoke(finalArgs);
        }
        else if (handlerParams.Length == 2 && handlerParams[0].ParameterType == typeof(IServiceProvider))
        {
            invokeResult = selectedDescriptor.Handler.DynamicInvoke(new object?[] { sp, finalArgs });
        }
        else
        {
            throw new InvalidOperationException("Unsupported handler signature.");
        }

        if (invokeResult is Task t)
        {
            await t.ConfigureAwait(false);
        }

        return 0;
    }

    private static void PrintHelp(CommandRegistry registry)
    {
        Console.WriteLine("Available commands:");
        foreach (var d in registry.Descriptors.OrderBy(d => d.Name))
        {
            var parts = new List<string>();
            foreach (var p in d.Parameters)
            {
                var opt = p.GetCustomAttribute<OptionAttribute>();
                if (opt != null)
                {
                    var alias = string.IsNullOrWhiteSpace(opt.Name) ? ToKebabCase(p.Name!) : opt.Name!;
                    var desc = string.IsNullOrWhiteSpace(opt.Description) ? string.Empty : $" : {opt.Description}";
                    parts.Add($"[--{alias}{desc}]");
                }
                else
                {
                    parts.Add(p.Name!);
                }
            }
            Console.WriteLine($"  {d.Name} {string.Join(" ", parts)}    {d.Description}");
        }
        Console.WriteLine("Use --help for detailed help if supported.");
    }

    private static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var chars = new List<char>(name.Length * 2);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    chars.Add('-');
                }
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }
        return new string(chars.ToArray());
    }
}

// Removed static fallback; DI is the single source of truth.
