namespace Commanda;

using System.CommandLine;
using System.Reflection;

internal sealed class CommandRegistry
{
    private readonly List<CommandDescriptor> _descriptors = new();

    public void Add(CommandDescriptor descriptor) => _descriptors.Add(descriptor);

    public IReadOnlyList<CommandDescriptor> Descriptors => _descriptors;
}

internal sealed class CommandDescriptor
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Delegate Handler { get; init; }
    public ParameterInfo[] Parameters { get; init; } = Array.Empty<ParameterInfo>();
}
