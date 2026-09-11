using Mono.Cecil;
using Mono.Collections.Generic;

// usage: PatchCheck <Hoard.dll> <dir with game+bepinex dlls>...
var pluginPath = args[0];
var searchDirs = args.Skip(1).ToArray();
var resolver = new DefaultAssemblyResolver();
foreach (var d in searchDirs) resolver.AddSearchDirectory(d);
var plugin = AssemblyDefinition.ReadAssembly(pluginPath, new ReaderParameters { AssemblyResolver = resolver });

int errors = 0, checkedPatches = 0, dynamicTargets = 0;
var specialParams = new HashSet<string> { "__instance", "__result", "__state", "__exception", "__runOriginal", "__originalMethod", "__args" };

foreach (var type in plugin.MainModule.GetTypes())
{
    var attrs = type.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").ToList();
    if (attrs.Count == 0) continue;
    var patchMethods = type.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer" or "Transpiler"
        || m.CustomAttributes.Any(a => a.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyFinalizer")).ToList();
    if (type.Methods.Any(m => m.Name == "TargetMethods" || m.Name == "TargetMethod"))
    {
        dynamicTargets++;
        Console.WriteLine($"  skip (dynamic targets): {type.FullName}");
        continue;
    }

    TypeReference declaring = null; string methodName = null; List<TypeReference> argTypes = null;
    foreach (var a in attrs)
    {
        foreach (var arg in a.ConstructorArguments)
        {
            if (arg.Type.FullName == "System.Type") declaring = (TypeReference)arg.Value;
            else if (arg.Type.FullName == "System.String") methodName = (string)arg.Value;
            else if (arg.Type.FullName == "System.Type[]")
                argTypes = ((CustomAttributeArgument[])arg.Value).Select(x => (TypeReference)x.Value).ToList();
        }
    }
    if (declaring == null || methodName == null)
    {
        Console.WriteLine($"  ?? could not read attribute on {type.FullName}");
        continue;
    }
    TypeDefinition target;
    try { target = declaring.Resolve(); }
    catch (Exception e) { Console.WriteLine($"FAIL {type.FullName}: cannot resolve {declaring.FullName}: {e.Message}"); errors++; continue; }
    if (target == null) { Console.WriteLine($"FAIL {type.FullName}: type {declaring.FullName} not found"); errors++; continue; }

    var candidates = new List<MethodDefinition>();
    for (var t = target; t != null; t = t.BaseType?.Resolve())
    {
        candidates.AddRange(t.Methods.Where(m => m.Name == methodName));
        if (candidates.Count > 0) break;
    }
    if (argTypes != null)
        candidates = candidates.Where(m => m.Parameters.Count == argTypes.Count && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(argTypes.Select(x => x.FullName))).ToList();
    checkedPatches++;
    if (candidates.Count == 0)
    {
        Console.WriteLine($"FAIL {type.FullName}: {declaring.Name}.{methodName}({(argTypes == null ? "" : string.Join(", ", argTypes.Select(x => x.Name)))}) not found");
        errors++;
        continue;
    }
    if (candidates.Count > 1)
    {
        Console.WriteLine($"FAIL {type.FullName}: {declaring.Name}.{methodName} is ambiguous ({candidates.Count} overloads) - specify argument types");
        errors++;
        continue;
    }
    var original = candidates[0];
    var originalParams = original.Parameters.Select(p => p.Name).ToHashSet();
    foreach (var pm in patchMethods)
    {
        foreach (var p in pm.Parameters)
        {
            if (specialParams.Contains(p.Name) || p.Name.StartsWith("___")) continue;
            if (!originalParams.Contains(p.Name))
            {
                Console.WriteLine($"FAIL {type.FullName}.{pm.Name}: parameter '{p.Name}' is not a parameter of {declaring.Name}.{methodName}({string.Join(", ", original.Parameters.Select(x => x.ParameterType.Name + " " + x.Name))})");
                errors++;
            }
        }
        if (pm.Parameters.Any(p => p.Name == "__instance") && original.IsStatic)
        {
            Console.WriteLine($"FAIL {type.FullName}.{pm.Name}: __instance used on static {declaring.Name}.{methodName}");
            errors++;
        }
    }
    Console.WriteLine($"  ok   {declaring.Name}.{methodName}  <- {type.Name}");
}
Console.WriteLine($"\n{checkedPatches} patch targets checked, {dynamicTargets} dynamic skipped, {errors} problem(s)");
return errors == 0 ? 0 : 1;
