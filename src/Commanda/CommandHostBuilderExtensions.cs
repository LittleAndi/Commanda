using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Commanda;

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

        if (args.Length == 0)
        {
            PrintHelp(registry);
            return 0;
        }

        var bindingMap = BuildBindingMap(registry);
        var invokedName = args[0];

        if (string.IsNullOrEmpty(invokedName) || !bindingMap.TryGetValue(invokedName, out var bindInfo))
        {
            Console.Error.WriteLine("Unknown or missing command.\n");
            PrintHelp(registry);
            return 1;
        }

        var finalArgs = new object?[bindInfo.Parameters.Length];
        ParseCommandLineArguments(args.Skip(1).ToArray(), bindInfo, finalArgs);

        if (!ApplyDefaultsAndValidate(bindInfo.Parameters, finalArgs))
        {
            return 1;
        }

        ResolveDependencyInjectionParameters(host.Services, bindInfo.DiParams, finalArgs);

        var selectedDescriptor = registry.Descriptors.First(d => d.Name == invokedName);
        await InvokeCommandHandler(selectedDescriptor, finalArgs, host.Services);

        return 0;
    }

    private record ParameterBinding(
        int Index,
        Type Type,
        bool IsCliArgument,
        bool IsOption,
        string? Alias = null
    );

    private record DependencyInjectionParameter(
        int Index,
        Type Type
    );

    private record BindingInfo(
        ParameterInfo[] Parameters,
        List<ParameterBinding> Bindings,
        List<DependencyInjectionParameter> DiParams
    );

    private class TokenParser
    {
        private readonly string[] _tokens;
        private int _currentIndex;

        public TokenParser(string[] tokens)
        {
            _tokens = tokens;
            _currentIndex = 0;
        }

        public bool HasMoreTokens => _currentIndex < _tokens.Length;
        public string CurrentToken => _tokens[_currentIndex];

        public void Advance() => _currentIndex++;

        public bool TryPeekNext(out string? nextToken)
        {
            if (_currentIndex + 1 < _tokens.Length)
            {
                nextToken = _tokens[_currentIndex + 1];
                return true;
            }
            nextToken = null;
            return false;
        }

        public bool PeekNextStartsWith(string prefix)
        {
            return TryPeekNext(out var next) && next != null && next.StartsWith(prefix, StringComparison.Ordinal);
        }

        public string? ConsumeNext()
        {
            if (TryPeekNext(out var next))
            {
                _currentIndex++;
                return next;
            }
            return null;
        }
    }

    private class ParameterContext
    {
        public ParameterInfo Parameter { get; }
        public object?[] FinalArgs { get; }
        public int Index { get; }

        public ParameterContext(ParameterInfo parameter, object?[] finalArgs, int index)
        {
            Parameter = parameter;
            FinalArgs = finalArgs;
            Index = index;
        }

        public object? CurrentValue => FinalArgs[Index];
        public void SetValue(object? value) => FinalArgs[Index] = value;
    }

    private static Dictionary<string, BindingInfo> BuildBindingMap(CommandRegistry registry)
    {
        var bindingMap = new Dictionary<string, BindingInfo>();

        foreach (var descriptor in registry.Descriptors)
        {
            var parameters = descriptor.Parameters;
            var bindings = new List<ParameterBinding>();
            var diParams = new List<DependencyInjectionParameter>();

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pType = p.ParameterType;

                if (pType != typeof(string) && !pType.IsValueType)
                {
                    diParams.Add(new DependencyInjectionParameter(i, pType));
                    continue;
                }

                var optAttr = p.GetCustomAttribute<OptionAttribute>();
                if (optAttr != null)
                {
                    var alias = "--" + (string.IsNullOrWhiteSpace(optAttr.Name) ? ToKebabCase(p.Name!) : optAttr.Name);
                    bindings.Add(new ParameterBinding(i, pType, IsCliArgument: true, IsOption: true, Alias: alias));
                }
                else
                {
                    bindings.Add(new ParameterBinding(i, pType, IsCliArgument: true, IsOption: false));
                }
            }

            bindingMap[descriptor.Name] = new BindingInfo(parameters, bindings, diParams);
        }

        return bindingMap;
    }

    private static void ParseCommandLineArguments(string[] tokens, BindingInfo bindInfo, object?[] finalArgs)
    {
        var parser = new TokenParser(tokens);
        var optMap = bindInfo.Bindings
            .Where(b => b.IsOption && b.Alias != null)
            .ToDictionary(b => b.Alias!, b => b);

        var argQueue = new Queue<ParameterBinding>(
            bindInfo.Bindings.Where(b => !b.IsOption));

        while (parser.HasMoreTokens)
        {
            var token = parser.CurrentToken;

            if (token.StartsWith("--"))
            {
                if (optMap.TryGetValue(token, out var binding))
                {
                    finalArgs[binding.Index] = binding.Type == typeof(bool)
                        ? ParseBooleanOption(parser)
                        : ParseTypedOption(parser, binding.Type);
                }
                parser.Advance();
            }
            else if (argQueue.Count > 0)
            {
                var binding = argQueue.Dequeue();
                finalArgs[binding.Index] = TryConvertType(token, binding.Type);
                parser.Advance();
            }
            else
            {
                parser.Advance();
            }
        }
    }

    private static bool ParseBooleanOption(TokenParser parser)
    {
        bool val = true;
        if (!parser.PeekNextStartsWith("--"))
        {
            var next = parser.ConsumeNext();
            if (next != null && bool.TryParse(next, out var parsed))
            {
                val = parsed;
            }
        }
        return val;
    }

    private static object? ParseTypedOption(TokenParser parser, Type targetType)
    {
        if (!parser.PeekNextStartsWith("--"))
        {
            var next = parser.ConsumeNext();
            if (next != null)
            {
                return TryConvertType(next, targetType);
            }
        }
        return null;
    }

    private static object? TryConvertType(string value, Type targetType)
    {
        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return null;
        }
    }

    private static bool ApplyDefaultsAndValidate(ParameterInfo[] parameters, object?[] finalArgs)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var context = new ParameterContext(parameters[i], finalArgs, i);

            if (!NeedsDefaultValue(context))
            {
                continue;
            }

            if (!ApplyDefaultForParameter(context))
            {
                return false;
            }
        }
        return true;
    }

    private static bool NeedsDefaultValue(ParameterContext context)
    {
        if (context.CurrentValue != null) return false;
        if (context.Parameter.ParameterType != typeof(string) && !context.Parameter.ParameterType.IsValueType) return false;
        return true;
    }

    private static bool ApplyDefaultForParameter(ParameterContext context)
    {
        var optAttr = context.Parameter.GetCustomAttribute<OptionAttribute>();

        return optAttr != null
            ? ApplyOptionDefault(context, optAttr)
            : ApplyArgumentDefault(context);
    }

    private static bool ApplyOptionDefault(ParameterContext context, OptionAttribute optAttr)
    {
        if (context.Parameter.ParameterType == typeof(bool))
        {
            context.SetValue(context.Parameter.HasDefaultValue ? context.Parameter.DefaultValue : false);
        }
        else if (context.Parameter.HasDefaultValue)
        {
            context.SetValue(context.Parameter.DefaultValue);
        }
        else
        {
            var optionName = string.IsNullOrWhiteSpace(optAttr.Name) ? ToKebabCase(context.Parameter.Name!) : optAttr.Name;
            Console.Error.WriteLine($"Missing required option '--{optionName}'.");
            return false;
        }
        return true;
    }

    private static bool ApplyArgumentDefault(ParameterContext context)
    {
        if (context.Parameter.HasDefaultValue)
        {
            context.SetValue(context.Parameter.DefaultValue);
        }
        else
        {
            Console.Error.WriteLine($"Missing required argument '{context.Parameter.Name}'.");
            return false;
        }
        return true;
    }

    private static void ResolveDependencyInjectionParameters(IServiceProvider serviceProvider, List<DependencyInjectionParameter> diParams, object?[] finalArgs)
    {
        foreach (var param in diParams)
        {
            finalArgs[param.Index] = serviceProvider.GetService(param.Type);
        }
    }

    private static async Task InvokeCommandHandler(CommandDescriptor descriptor, object?[] finalArgs, IServiceProvider serviceProvider)
    {
        var handlerParams = descriptor.Handler.Method.GetParameters();
        object? invokeResult;

        if (handlerParams.Length == finalArgs.Length)
        {
            invokeResult = descriptor.Handler.DynamicInvoke(finalArgs);
        }
        else if (handlerParams.Length == 2 && handlerParams[0].ParameterType == typeof(IServiceProvider))
        {
            invokeResult = descriptor.Handler.DynamicInvoke(new object?[] { serviceProvider, finalArgs });
        }
        else
        {
            throw new InvalidOperationException("Unsupported handler signature.");
        }

        if (invokeResult is Task t)
        {
            await t.ConfigureAwait(false);
        }
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
                if (ShouldInsertDashBeforeUpperCase(name, i))
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

    private static bool ShouldInsertDashBeforeUpperCase(string name, int index)
    {
        if (index == 0) return false;

        return IsPrecededByLowerCase(name, index) || IsFollowedByLowerCase(name, index);
    }

    private static bool IsPrecededByLowerCase(string name, int index)
    {
        return index > 0 && char.IsLower(name[index - 1]);
    }

    private static bool IsFollowedByLowerCase(string name, int index)
    {
        return index + 1 < name.Length && char.IsLower(name[index + 1]);
    }
}

// Removed static fallback; DI is the single source of truth.
