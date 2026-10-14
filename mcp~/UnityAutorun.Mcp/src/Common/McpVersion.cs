using System.Reflection;

namespace UnityAutorun.Mcp
{
    public static class McpVersion
    {
        public static string Current { get; } = Resolve();

        private static string Resolve()
        {
            string version = typeof(McpVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(version) ? "unknown" : version.Split('+')[0];
        }
    }
}
