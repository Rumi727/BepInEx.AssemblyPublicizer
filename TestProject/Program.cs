using TestLibrary;
using TestProject;

var internalClass = new InternalClass();
internalClass.PrivateMethod();
Console.WriteLine(internalClass.PrivateProperty);
internalClass.PrivateAutoProperty = 123;
Console.WriteLine(internalClass.PrivateAutoProperty);

internalClass.PrivateEvent += Handler;
internalClass.PrivateEvent -= Handler;
internalClass.InternalEvent += Handler;
internalClass.InternalEvent -= Handler;
internalClass.ProtectedEvent += Handler;
internalClass.ProtectedEvent -= Handler;

EventPublicizingTests.Run();

static void Handler()
{
}
