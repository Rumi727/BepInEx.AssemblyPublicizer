using AsmResolver.DotNet;
using BepInEx.AssemblyPublicizer;
using FieldAttributes = AsmResolver.PE.DotNet.Metadata.Tables.Rows.FieldAttributes;
using MethodAttributes = AsmResolver.PE.DotNet.Metadata.Tables.Rows.MethodAttributes;

namespace TestProject;

internal static class EventPublicizingTests
{
    internal static void Run()
    {
        var assemblyPath = typeof(TestLibrary.InternalClass).Assembly.Location;
        var testOptions = new[]
        {
            new AssemblyPublicizerOptions(),
            new AssemblyPublicizerOptions { Target = PublicizeTarget.Methods },
            new AssemblyPublicizerOptions { Target = PublicizeTarget.Types | PublicizeTarget.Fields },
            new AssemblyPublicizerOptions { PublicizeCompilerGenerated = true },
            new AssemblyPublicizerOptions { IncludeOriginalAttributesAttribute = false },
            new AssemblyPublicizerOptions { IncludeOriginalAttributesAttribute = false, PublicizeCompilerGenerated = true },
        };

        foreach (var options in testOptions)
        {
            var assembly = AssemblyDefinition.FromFile(assemblyPath);
            var type = assembly.ManifestModule!.GetAllTypes().Single(t => t.FullName == "TestLibrary.InternalClass");

            foreach (var eventDefinition in type.Events)
            {
                var addMethod = eventDefinition.AddMethod!;
                var removeMethod = eventDefinition.RemoveMethod!;
                Assert(addMethod.IsCompilerGenerated(), $"{eventDefinition.Name}: add must be compiler-generated");
                Assert(removeMethod.IsCompilerGenerated(), $"{eventDefinition.Name}: remove must be compiler-generated");
                var originalAccess = GetOriginalEventAccess(eventDefinition);
                Assert((addMethod.Attributes & MethodAttributes.MemberAccessMask) == originalAccess,
                    $"{eventDefinition.Name}: unexpected original add access");
                Assert((removeMethod.Attributes & MethodAttributes.MemberAccessMask) == originalAccess,
                    $"{eventDefinition.Name}: unexpected original remove access");
            }

            AssemblyPublicizer.Publicize(assembly, options);

            // Re-read serialized metadata rather than checking only the modified object graph.
            using var stream = new MemoryStream();
            assembly.ManifestModule!.Write(stream);
            VerifyAssembly(AssemblyDefinition.FromBytes(stream.ToArray()), options);

            // Exercise the file overload and its production PE writer as well.
            var outputPath = Path.Combine(Path.GetTempPath(), $"event-publicizing-{Guid.NewGuid():N}.dll");
            try
            {
                AssemblyPublicizer.Publicize(assemblyPath, outputPath, options);
                VerifyAssembly(AssemblyDefinition.FromFile(outputPath), options);
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        // C# has no fire accessor syntax. Attach one and mark an event compiler-generated in metadata.
        var specialAssembly = AssemblyDefinition.FromFile(assemblyPath);
        var specialType = specialAssembly.ManifestModule!.GetAllTypes().Single(t => t.FullName == "TestLibrary.InternalClass");
        var generatedEvent = specialType.Events.Single(e => e.Name == "PrivateEvent");
        var compilerGeneratedConstructor = generatedEvent.AddMethod!.CustomAttributes
            .Single(a => a.Constructor?.DeclaringType?.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute").Constructor;
        generatedEvent.CustomAttributes.Add(new CustomAttribute(compilerGeneratedConstructor));

        var sourceEvent = specialType.Events.Single(e => e.Name == "InternalEvent");
        var fireMethod = specialType.Methods.Single(m => m.Name == "PrivateMethod");
        fireMethod.CustomAttributes.Add(new CustomAttribute(compilerGeneratedConstructor));
        sourceEvent.SetSemanticMethods(sourceEvent.AddMethod, sourceEvent.RemoveMethod, fireMethod);

        AssemblyPublicizer.Publicize(specialAssembly);

        Assert(generatedEvent.AddMethod!.IsPrivate, "Compiler-generated event: add must remain private");
        Assert(generatedEvent.RemoveMethod!.IsPrivate, "Compiler-generated event: remove must remain private");
        Assert(sourceEvent.AddMethod!.IsPublic && sourceEvent.RemoveMethod!.IsPublic, "Source-declared event: add/remove must be public");
        Assert(sourceEvent.FireMethod!.IsPrivate, "Compiler-generated fire accessor must remain private");

        Console.WriteLine("Event publicizing regression tests passed.");
    }

    private static MethodAttributes GetOriginalEventAccess(EventDefinition eventDefinition)
    {
        return eventDefinition.Name?.ToString() switch
        {
            "PrivateEvent" => MethodAttributes.Private,
            "InternalEvent" => MethodAttributes.Assembly,
            "ProtectedEvent" => MethodAttributes.Family,
            "ProtectedInternalEvent" => MethodAttributes.FamilyOrAssembly,
            "PrivateProtectedEvent" => MethodAttributes.FamilyAndAssembly,
            _ => throw new InvalidOperationException($"Unexpected event: {eventDefinition.Name}"),
        };
    }

    private static void VerifyAssembly(AssemblyDefinition assembly, AssemblyPublicizerOptions options)
    {
        var module = assembly.ManifestModule!;
        var type = module.GetAllTypes().Single(t => t.FullName == "TestLibrary.InternalClass");
        Assert(type.Events.Count == 5, "All five event accessibilities must be tested");

        foreach (var eventDefinition in type.Events)
        {
            var originalAccess = GetOriginalEventAccess(eventDefinition);
            VerifyMethod(eventDefinition.AddMethod!, originalAccess, options);
            VerifyMethod(eventDefinition.RemoveMethod!, originalAccess, options);
            VerifyOriginalAttributes(eventDefinition.CustomAttributes, eventDefinition.Name!.ToString(), (int)originalAccess, false);

            var backingField = type.Fields.Single(f => f.Name == eventDefinition.Name);
            Assert(backingField.IsPrivate, $"{eventDefinition.Name}: backing field must remain private");
            VerifyOriginalAttributes(backingField.CustomAttributes, backingField.Name!.ToString(), (int)FieldAttributes.Private, false);
        }

        var property = type.Properties.Single(p => p.Name == "PrivateAutoProperty");
        VerifyMethod(property.GetMethod!, MethodAttributes.Private, options);
        VerifyMethod(property.SetMethod!, MethodAttributes.Private, options);
        VerifyMethod(type.Properties.Single(p => p.Name == "PrivateProperty").GetMethod!, MethodAttributes.Private, options);
        VerifyMethod(type.Methods.Single(m => m.Name == "PrivateMethod"), MethodAttributes.Private, options);

        var field = module.GetAllTypes().Single(t => t.FullName == "TestLibrary.SecondClass").Fields.Single(f => f.Name == "_field");
        var publicizeFields = (options.Target & PublicizeTarget.Fields) != 0;
        var expectedFieldAccess = publicizeFields ? FieldAttributes.Public : FieldAttributes.Private;
        Assert((field.Attributes & FieldAttributes.FieldAccessMask) == expectedFieldAccess, $"{field.Name}: unexpected access");
        VerifyOriginalAttributes(field.CustomAttributes, field.Name!.ToString(), (int)FieldAttributes.Private,
            publicizeFields && options.IncludeOriginalAttributesAttribute);
    }

    private static void VerifyMethod(MethodDefinition method, MethodAttributes originalAccess, AssemblyPublicizerOptions options)
    {
        var publicizeMethods = (options.Target & PublicizeTarget.Methods) != 0;
        var expectedAccess = publicizeMethods ? MethodAttributes.Public : originalAccess;
        Assert(method.IsPublic == publicizeMethods, $"{method.Name}: unexpected public access");
        Assert((method.Attributes & MethodAttributes.MemberAccessMask) == expectedAccess, $"{method.Name}: unexpected access");

        VerifyOriginalAttributes(method.CustomAttributes, method.Name!.ToString(), (int)originalAccess,
            publicizeMethods && options.IncludeOriginalAttributesAttribute);
    }

    private static void VerifyOriginalAttributes(IEnumerable<CustomAttribute> customAttributes, string memberName, int originalAccess, bool expected)
    {
        var attributes = customAttributes
            .Where(a => a.Constructor?.DeclaringType?.FullName == "BepInEx.AssemblyPublicizer.OriginalAttributesAttribute")
            .ToArray();
        var expectedCount = expected ? 1 : 0;
        Assert(attributes.Length == expectedCount, $"{memberName}: unexpected original attributes count");

        if (expectedCount != 0)
        {
            Assert(attributes[0].Signature!.FixedArguments.Count == 1, $"{memberName}: expected one original attributes argument");
            Assert(Convert.ToInt32(attributes[0].Signature!.FixedArguments[0].Element) == originalAccess,
                $"{memberName}: original access was not preserved");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
