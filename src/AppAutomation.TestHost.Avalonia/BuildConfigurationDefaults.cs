using System.Reflection;

namespace AppAutomation.TestHost.Avalonia;

public static class BuildConfigurationDefaults
{
    public static string ForAssembly(Assembly? assembly = null)
    {
        var targetAssembly = assembly ?? Assembly.GetCallingAssembly();
        var configurationAttribute = targetAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
        if (!string.IsNullOrWhiteSpace(configurationAttribute?.Configuration))
        {
            return configurationAttribute.Configuration.Trim();
        }

#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
