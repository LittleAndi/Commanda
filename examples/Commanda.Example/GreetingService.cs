namespace Commanda.Example;

public class GreetingService
{
    public void SayHello() => Console.WriteLine("Hello from GreetingService!");
    public Task SayHelloAsync(string name)
    {
        Console.WriteLine($"Hello {name} from async service!");
        return Task.CompletedTask;
    }
}
