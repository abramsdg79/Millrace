using System.Reflection;
using System.Runtime.Loader;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>Loads catalogue modules from assemblies named on the command line.</summary>
internal static class ModuleLoader
{
    /// <summary>Adds every module found in <paramref name="paths"/>, in order. False, with one line in <paramref name="problem"/>, on the first failure.</summary>
    public static bool TryLoad(IReadOnlyList<string> paths, CatalogueBuilder builder, out string problem)
    {
        foreach (string given in paths)
        {
            string path = Path.GetFullPath(given);
            if (!File.Exists(path))
            {
                problem = $"Cannot load assembly '{given}': the file does not exist.";
                return false;
            }

            List<Type> modules;
            try
            {
                Assembly assembly = new PluginLoadContext(path).LoadFromAssemblyPath(path);
                modules = assembly.GetExportedTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false }
                             && typeof(ICatalogueModule).IsAssignableFrom(t)
                             && t.GetConstructor(Type.EmptyTypes) is not null)
                    .OrderBy(t => t.FullName, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or ReflectionTypeLoadException or TypeLoadException)
            {
                problem = $"Cannot load assembly '{given}': {ex.Message}";
                return false;
            }

            if (modules.Count == 0)
            {
                problem =
                    $"Assembly '{given}' contains no catalogue module. A module is a public, non-abstract class that implements " +
                    $"{nameof(ICatalogueModule)} and has a public parameterless constructor.";
                return false;
            }

            foreach (Type type in modules)
            {
                var module = (ICatalogueModule)Activator.CreateInstance(type)!;
                try
                {
                    builder.Add(module);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    problem = $"Module '{module.Name}' from '{given}' could not be registered: {ex.Message}";
                    return false;
                }
            }
        }

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// Resolves a plugin's private dependencies from beside the plugin, and
    /// declines anything the host ships: the host's copy of Dse.Core is what makes
    /// the plugin's ICatalogueModule the same type as ours.
    /// </summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            {
                return null; // Fall through to the default context: the host's copy.
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
