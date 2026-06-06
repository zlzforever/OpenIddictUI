using System.Reflection;

namespace OpenIddictUI.Plugins;

public static class PluginLoader
{
    private static readonly List<Type> Plugins = new();

    public static void Load(IHostApplicationBuilder builder, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PluginLoader");

        if (!Directory.Exists("Plugins"))
        {
            logger.LogDebug("PluginLoader: 'Plugins' directory not found, skipping");
            return;
        }

        foreach (var dll in Directory.GetFiles("Plugins", "*.dll"))
        {
            logger.LogInformation("PluginLoader: loading {Dll}", dll);
            var assembly = Assembly.LoadFrom(dll);
            var pluginTypes = assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase));

            foreach (var type in pluginTypes)
            {
                logger.LogInformation("PluginLoader: found plugin {Type}", type.FullName);
                var method = type.GetMethod("Load");
                method?.Invoke(null, [builder]);
                Plugins.Add(type);
            }
        }
    }

    public static void Use(WebApplication app, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PluginLoader");

        if (!Directory.Exists("Plugins"))
        {
            logger.LogDebug("PluginLoader: 'Plugins' directory not found, no plugins to invoke");
            return;
        }

        foreach (var plugin in Plugins)
        {
            logger.LogInformation("PluginLoader: invoking {Plugin}.Use()", plugin.FullName);
            var method = plugin.GetMethod("Use");
            method?.Invoke(null, [app]);
        }
    }
}