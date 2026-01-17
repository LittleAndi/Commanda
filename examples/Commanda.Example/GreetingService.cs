namespace Commanda.Example;

public class GreetingService
{
    public static void SayHello() => Console.WriteLine("Hello from GreetingService!");
    public static Task SayHelloAsync(string name, bool excited = false)
    {
        var excitedSuffix = excited ? "!" : ".";
        Console.WriteLine($"Hello {name} from async service{excitedSuffix}");
        return Task.CompletedTask;
    }
}
