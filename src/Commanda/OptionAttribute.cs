namespace Commanda;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class OptionAttribute : Attribute
{
    public OptionAttribute() { }
    public OptionAttribute(string name) => Name = name;

    public string? Name { get; }
    public string? Description { get; set; }
}
