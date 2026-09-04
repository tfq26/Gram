using System.Runtime.Loader;
using System.Text.Json;
using Gram.Blazor.Shared;

if (args.Length != 1) return 2;
var packageRoot = Path.GetFullPath(args[0]);
var assemblyPath = Path.Combine(packageRoot, "Connector.Validation.dll");
var manifestPath = Path.Combine(packageRoot, "connector.json");
if (!File.Exists(assemblyPath) || !File.Exists(manifestPath)) return 3;
var request = JsonSerializer.Deserialize<ConnectorHostExecuteRequest>(await Console.In.ReadToEndAsync(), BackendJson.Options);
var manifest = JsonSerializer.Deserialize<ConnectorManifest>(await File.ReadAllTextAsync(manifestPath), BackendJson.Options);
if (request is null || manifest?.ExecutionPolicy is null || !manifest.Id.Equals(request.Action.ConnectorId, StringComparison.OrdinalIgnoreCase)) return 4;
var context = new AssemblyLoadContext($"connector-worker-{request.PackageId}", true);
try
{
    var assembly = context.LoadFromAssemblyPath(assemblyPath);
    var type = assembly.GetTypes().FirstOrDefault(t => typeof(IConnectedConnector).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);
    if (type is null) return 5;
    var connector = (IConnectedConnector)Activator.CreateInstance(type)!;
    if (!connector.Actions.Any(action => action.Id.Equals(request.Action.ActionId, StringComparison.OrdinalIgnoreCase))) return 6;
    await Console.Out.WriteAsync(JsonSerializer.Serialize(await connector.ExecuteAsync(request.Action), BackendJson.Options));
    return 0;
}
catch (Exception ex)
{
    await Console.Out.WriteAsync(JsonSerializer.Serialize(new ConnectorActionResult(false, "worker_error", ex.GetType().Name), BackendJson.Options));
    return 7;
}
finally { context.Unload(); }
