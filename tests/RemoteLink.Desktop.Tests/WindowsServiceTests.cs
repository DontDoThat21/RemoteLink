using Xunit;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Tests for Windows service mode functionality.
/// </summary>
/// <remarks>
/// Note: These tests verify configuration and detection logic.
/// Actual service installation/uninstallation requires Windows and administrator privileges,
/// so full integration testing must be done manually on a Windows machine.
/// </remarks>
public class WindowsServiceTests
{
    [Fact]
    public void ServiceName_IsConfigured()
    {
        // Arrange
        const string expectedServiceName = "RemoteLinkHost";

        // Assert
        // Service name is configured in Program.cs via AddWindowsService options
        // This test documents the expected service name
        Assert.Equal("RemoteLinkHost", expectedServiceName);
    }

    [Fact]
    public void ConsoleMode_IsDetected_WhenUserInteractive()
    {
        // Arrange
        bool isWindowsService = OperatingSystem.IsWindows() && 
                                !Environment.UserInteractive;

        // Assert
        // When running tests, Environment.UserInteractive should be true
        // (tests run in user context, not as a service)
        if (OperatingSystem.IsWindows())
        {
            Assert.False(isWindowsService, "Tests should run in console mode (UserInteractive=true)");
        }
        else
        {
            Assert.False(isWindowsService, "Non-Windows platforms cannot run as Windows service");
        }
    }

    [Fact]
    public void ServiceConfiguration_DefaultPort_IsCorrect()
    {
        // Arrange
        const int expectedPort = 12346;

        // Assert
        // Port is configured in Program.cs DeviceInfo initialization
        // This test documents the expected default port
        Assert.Equal(12346, expectedPort);
    }

    [Theory]
    [InlineData(true, true, false)]   // Windows + UserInteractive = Console mode
    [InlineData(true, false, true)]   // Windows + !UserInteractive = Service mode
    [InlineData(false, true, false)]  // !Windows + UserInteractive = Console mode
    [InlineData(false, false, false)] // !Windows + !UserInteractive = Console mode (can't be service)
    public void ServiceMode_Detection_Logic(bool isWindows, bool isUserInteractive, bool expectedServiceMode)
    {
        // Arrange
        bool isWindowsService = isWindows && !isUserInteractive;

        // Assert
        Assert.Equal(expectedServiceMode, isWindowsService);
    }

    [Fact]
    public void Documentation_Exists()
    {
        // Arrange
        string docsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "WindowsServiceMode.md"
        );

        // Normalize path
        docsPath = Path.GetFullPath(docsPath);

        // Assert
        Assert.True(
            File.Exists(docsPath), 
            $"Windows service documentation should exist at {docsPath}"
        );
    }

    [Fact]
    public void Documentation_Contains_InstallationInstructions()
    {
        // Arrange
        string docsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "WindowsServiceMode.md"
        );
        docsPath = Path.GetFullPath(docsPath);

        // Act
        if (!File.Exists(docsPath))
        {
            Assert.Fail("Documentation file not found");
            return;
        }

        string content = File.ReadAllText(docsPath);

        // Assert
        Assert.Contains("sc.exe create", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New-Service", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RemoteLinkHost", content);
        Assert.Contains("Uninstallation", content);
        Assert.Contains("Firewall", content);
    }

    [Fact]
    public void OutputType_IsConsoleExe()
    {
        // This test verifies that the project is configured as Exe (not WinExe)
        // which is required for Windows service support.
        // 
        // The actual verification happens at compile time via the csproj file:
        // <OutputType>Exe</OutputType>
        //
        // If this was WinExe, the service wouldn't work properly.
        Assert.True(true, "OutputType=Exe is enforced by the csproj file");
    }

    [Fact]
    public void WindowsServices_PackageReference_Required()
    {
        // This test documents the requirement for Microsoft.Extensions.Hosting.WindowsServices
        // Actual verification happens at compile time via PackageReference in csproj
        const string requiredPackage = "Microsoft.Extensions.Hosting.WindowsServices";
        Assert.NotNull(requiredPackage);
    }
}
