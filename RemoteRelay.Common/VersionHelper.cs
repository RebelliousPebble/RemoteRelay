using System.Reflection;

namespace RemoteRelay.Common;

public static class VersionHelper
{
    public static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
