
using Destructurama.Attributed;
using System.Reflection;
namespace Unicore.Common.OpenTelemetry.Helpers;
public static class LogMaskedRegistry
{
    // Type name => field/property names with [LogMasked]
    private static readonly Dictionary<string, HashSet<string>> _maskedFields = new();

    public static void Initialize(params Assembly[] assemblies)
    {
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            var maskableMembers = type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<LogMaskedAttribute>() != null);

            if (maskableMembers.Any())
            {
                var names = maskableMembers.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _maskedFields[type.Name] = names;
            }
        }
    }

    public static HashSet<string>? GetMaskedMembers(string typeName)
    {
        return _maskedFields.TryGetValue(typeName, out var members) ? members : null;
    }
}