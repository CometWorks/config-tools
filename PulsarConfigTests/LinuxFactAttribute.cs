using Xunit;

namespace PulsarConfigTests;

internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Linux permissions, processes or legacy layout."; }
}
internal sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Linux permissions, processes or legacy layout."; }
}

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows process guard."; }
}
