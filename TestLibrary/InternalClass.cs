namespace TestLibrary;

internal class InternalClass
{
    private void PrivateMethod()
    {
        Console.WriteLine("InternalClass.PrivateMethod");
    }

    private int PrivateProperty => 1;

    private int PrivateAutoProperty { get; set; }

    private event Action? PrivateEvent;

    internal event Action? InternalEvent;

    protected event Action? ProtectedEvent;

    protected internal event Action? ProtectedInternalEvent;

    private protected event Action? PrivateProtectedEvent;
}

public class SecondClass
{
    private int _field;

    private int Property { get; set; }

    private int Method()
    {
        return 0;
    }
}
