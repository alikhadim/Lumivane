using Lumivane.Core;

namespace Lumivane.Platform;

public static class PlatformFactory
{
    public static IPlatform Create(string? dataDir = null)
    {
        if (OperatingSystem.IsWindows()) return new Windows.WindowsPlatform(dataDir);
        if (OperatingSystem.IsLinux()) return new Linux.LinuxPlatform(dataDir);
        return new NullPlatform(dataDir);
    }
}
