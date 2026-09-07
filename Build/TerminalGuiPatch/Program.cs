using System.Collections.Concurrent;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Build-only patch of the pinned library. Neither Cecil nor this tool is shipped.
using var assembly = AssemblyDefinition.ReadAssembly(args[0]);
if (
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[0])))
    != "05984557F5850A66A0F7EA26570FE905963AD7AD9F0D73FD4F50567797F107A5"
)
    throw new InvalidOperationException(
        "Reassess the input-queue patch before changing Terminal.Gui."
    );
var module = assembly.MainModule;
var queueDefinition = module.ImportReference(typeof(ConcurrentQueue<>));
var helper = new TypeDefinition(
    "Terminal.Gui",
    "ConfigToolsInputQueue",
    TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
    module.TypeSystem.Object
);
module.Types.Add(helper);

MethodReference QueueMethod(string name, TypeReference element)
{
    var type = new GenericInstanceType(queueDefinition);
    type.GenericArguments.Add(element);
    var original =
        name == ".ctor"
            ? module.ImportReference(typeof(ConcurrentQueue<>).GetConstructor(Type.EmptyTypes)!)
            : module.ImportReference(
                typeof(ConcurrentQueue<>).GetMethods().Single(m => m.Name == name)
            );
    var method = new MethodReference(original.Name, original.ReturnType, type) { HasThis = true };
    foreach (var parameter in original.Parameters)
        method.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
    return method;
}

// Null means shutdown, not an input event. Keep just one consumer per queue.
MethodDefinition Adapter(string name, bool enqueue)
{
    var method = new MethodDefinition(
        name,
        MethodAttributes.Assembly | MethodAttributes.Static,
        module.TypeSystem.Void
    );
    var valueType = new GenericParameter("T", method);
    method.GenericParameters.Add(valueType);
    var queue = new GenericInstanceType(queueDefinition);
    queue.GenericArguments.Add(valueType);
    method.Parameters.Add(new ParameterDefinition(queue));
    var il = method.Body.GetILProcessor();
    if (enqueue)
    {
        method.Parameters.Add(new ParameterDefinition(valueType));
        var end = il.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, valueType);
        il.Emit(OpCodes.Brfalse, end);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, QueueMethod("Enqueue", valueType));
        il.Append(end);
    }
    else
    {
        method.ReturnType = valueType;
        method.Body.InitLocals = true;
        var value = new VariableDefinition(valueType);
        method.Body.Variables.Add(value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, QueueMethod("TryDequeue", valueType));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);
    }
    helper.Methods.Add(method);
    return method;
}
var add = Adapter("Add", true);
var take = Adapter("Take", false);
var loop = module.GetType("Terminal.Gui.NetMainLoop");
var handler = loop.Methods.Single(m => m.Name == "NetInputHandler");

// This loop raced with MainIteration: Count > 0, UI drains queue, then Peek throws.
// Removing it is safe because Add now filters out shutdown's null result.
var instructions = handler.Body.Instructions;
if (
    !instructions.Any(i => i.Offset == 0x60 && i.Operand is MethodReference { Name: "Peek" })
    || !instructions.Any(i => i.Offset == 0x6d && i.OpCode == OpCodes.Brfalse_S)
)
    throw new InvalidOperationException("Terminal.Gui input loop changed; patch was not applied.");
foreach (var instruction in instructions.Where(i => i.Offset >= 0x40 && i.Offset < 0x6f))
{
    instruction.OpCode = OpCodes.Nop;
    instruction.Operand = null;
}
int queues = 0;
foreach (var type in new[] { loop, module.GetType("Terminal.Gui.NetEvents") })
{
    foreach (var field in type.Fields)
    {
        if (
            field.FieldType is not GenericInstanceType old
            || old.ElementType.FullName != "System.Collections.Generic.Queue`1"
        )
            continue;
        var replacement = new GenericInstanceType(queueDefinition);
        replacement.GenericArguments.Add(old.GenericArguments[0]);
        field.FieldType = replacement;
        queues++;
    }
    foreach (var method in type.Methods.Where(m => m.HasBody))
    foreach (var instruction in method.Body.Instructions)
    {
        if (
            instruction.Operand is not MethodReference call
            || call.DeclaringType is not GenericInstanceType queue
            || queue.ElementType.FullName != "System.Collections.Generic.Queue`1"
        )
            continue;
        var element = queue.GenericArguments[0];
        if (call.Name is "Enqueue" or "Dequeue")
        {
            var adapter = new GenericInstanceMethod(call.Name == "Enqueue" ? add : take);
            adapter.GenericArguments.Add(element);
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = adapter;
        }
        else if (call.Name is ".ctor" or "get_Count")
            instruction.Operand = QueueMethod(call.Name, element);
        else
            throw new InvalidOperationException("Unexpected queue call: " + call.FullName);
    }
}
if (queues != 2)
    throw new InvalidOperationException("Expected exactly two Terminal.Gui input queues.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
assembly.Write(args[1]);
Console.WriteLine(
    "Patched Terminal.Gui 1.19 input queues (single consumer, concurrent producers)."
);
