RemoteLink - Free Remote Desktop Solution
A free, open-source remote desktop application that allows you to remotely access your Windows desktop from any mobile device. RemoteLink provides a cost-effective, locally-hosted alternative to TeamViewer with peer-to-peer connectivity and no cloud dependencies.

🚀 Features
Cross-Platform Mobile Support: Windows desktop host with .NET MAUI mobile client supporting Android, iOS, macOS, and Windows
Local Network Discovery: Automatic discovery of devices on the local network
Real-Time Screen Sharing: Live desktop screen streaming to mobile device
Remote Input Control: Touch, mouse, and keyboard input from mobile to desktop
Secure Communication: Encrypted peer-to-peer connections
No Cloud Dependencies: Works entirely on local network
Free & Open Source: No subscription fees or licensing costs
📋 Project Status
This project is currently in initial development phase. The following core components have been implemented:

✅ Completed Features
Basic project structure and architecture
Network discovery service (UDP-based)
Core data models and interfaces
Desktop host application foundation
.NET MAUI mobile client foundation
Cross-platform compatibility layer
Dependency injection and logging setup
🚧 In Progress
Real-time communication service (TCP/SignalR)
Screen capture implementation (Windows-specific)
Input handling (Windows API integration)
.NET MAUI UI for remote control across all platforms
Authentication and pairing mechanism
Performance optimization
📋 Planned Features
End-to-end encryption
Multi-monitor support
File transfer capability
Connection quality optimization
Audio streaming
Session recording
Platform-specific optimizations (iOS, Android, macOS, Windows)
🏗️ Architecture
RemoteLink Solution
├── src/
│   ├── RemoteLink.Shared/          # Common interfaces, models, and services
│   ├── RemoteLink.Desktop/         # Windows desktop host application
│   └── RemoteLink.Mobile/          # .NET MAUI cross-platform mobile client
├── tests/
│   ├── RemoteLink.Shared.Tests/    # Unit tests for shared components
│   └── RemoteLink.Desktop.Tests/   # Unit tests for desktop application
└── docs/                           # Documentation and guides
Core Components
Shared Library (RemoteLink.Shared)

Network discovery service
Communication interfaces
Data models (DeviceInfo, ScreenData, InputEvent)
Cross-platform services
Desktop Host (RemoteLink.Desktop)

Screen capture service
Input handling service
Network broadcasting
Windows-specific implementations
Mobile Client (RemoteLink.Mobile)

.NET MAUI cross-platform application
Device discovery and connection
Remote input generation
Screen display and interaction
Touch-to-mouse translation
Platform-specific UI adaptations
🔧 Technical Requirements
Development Environment
.NET 8.0 SDK or later
Visual Studio 2022 17.8+ or Visual Studio Code with C# Dev Kit
.NET MAUI workload installed
Windows 10/11 (for desktop host development)
Platform-specific SDKs for target platforms:
Android SDK (API level 24+)
Xcode (for iOS/macOS development)
Runtime Requirements
Desktop Host: Windows 10/11 with .NET 8.0 Runtime
Mobile Client:
Android 7.0+ (API level 24)
iOS 12.0+
macOS 10.15+
Windows 10 version 1809+
Network: Local network connectivity between devices
🚀 Getting Started
Prerequisites
Install .NET 8.0 SDK
Install .NET MAUI workload:
bash
dotnet workload install maui
Clone this repository:
bash
git clone https://github.com/DontDoThat21/RemoteLink.git
cd RemoteLink
Building the Solution
bash
# Restore dependencies and build all projects
dotnet restore
dotnet build

# Run tests
dotnet test
Running the Desktop Host
bash
# Navigate to desktop project
cd src/RemoteLink.Desktop/RemoteLink.Desktop

# Run the desktop host
dotnet run
Running the Mobile Client
For Android:

bash
# Navigate to mobile project  
cd src/RemoteLink.Mobile/RemoteLink.Mobile

# Run on Android emulator or device
dotnet build -t:Run -f net8.0-android
For iOS:

bash
# Run on iOS simulator (requires macOS)
dotnet build -t:Run -f net8.0-ios
For Windows:

bash
# Run as Windows app
dotnet build -t:Run -f net8.0-windows10.0.19041.0
For macOS:

bash
# Run on macOS (requires macOS)
dotnet build -t:Run -f net8.0-maccatalyst
🔧 Configuration
Network Settings
Discovery Port: 12345 (UDP)
Desktop Host Port: 12346 (TCP)
Mobile Client Port: 12347 (TCP)
Broadcast Interval: 5 seconds
Device Timeout: 15 seconds
Performance Settings
Screen Capture Rate: 10 FPS (configurable)
Image Quality: 75% JPEG compression (configurable)
Default Resolution: 1920x1080 (auto-detected)
🧪 Testing
The project includes comprehensive unit tests for core functionality:

bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/RemoteLink.Shared.Tests/
🤝 Contributing
Contributions are welcome to RemoteLink! Please see Contributing Guidelines for details.

Development Workflow
Fork the repository
Create a feature branch: git checkout -b feature/new-feature
Make your changes and add tests
Ensure all tests pass: dotnet test
Commit your changes: git commit -m 'Add new feature'
Push to the branch: git push origin feature/new-feature
Submit a pull request
📝 License
This project is licensed under the MIT License - see the LICENSE file for details.

🆘 Support
Issues: GitHub Issues
Discussions: GitHub Discussions
Documentation: Wiki
🗺️ Roadmap
Phase 1: Core Foundation (Current)
Basic project structure ✅
Network discovery ✅
Core interfaces and models ✅
Phase 2: Basic Functionality (Next)
Real-time communication
Screen capture and streaming
Basic input handling
Cross-platform .NET MAUI UI
Phase 3: Enhanced Features
Platform-specific UI optimizations
Authentication and security
Performance optimization
iOS and macOS support
Phase 4: Advanced Features
Multi-monitor support
File transfer
Audio streaming
Advanced security features
Platform-specific integrations
🙏 Acknowledgments
Inspired by open-source remote desktop solutions
Built with modern .NET technologies and .NET MAUI
Community-driven development
Core Technology Stack
Primary Framework & Runtime
.NET 8.0 - Modern cross-platform framework
C# - Primary programming language with nullable reference types enabled
.NET MAUI - Cross-platform UI framework for mobile and desktop applications
Architecture & Patterns
Modular Architecture with three main components:

RemoteLink.Shared - Common interfaces, models, and services
RemoteLink.Desktop - Windows desktop host application
RemoteLink.Mobile - .NET MAUI cross-platform client application
Dependencies & Libraries
Microsoft.Extensions.Hosting (8.0.0) - Background service hosting
Microsoft.Extensions.DependencyInjection (8.0.0) - Dependency injection container
Microsoft.Extensions.Logging (8.0.0) - Structured logging
.NET MAUI (8.0.0) - Cross-platform UI framework
Microsoft.Maui.Controls - UI controls and layouts
Microsoft.Maui.Essentials - Cross-platform APIs
Testing Framework
xUnit (2.4.2) - Unit testing framework
Microsoft.NET.Test.Sdk (17.6.0) - Test SDK
Coverlet.collector (6.0.0) - Code coverage collection
Network & Communication
UDP-based network discovery (custom implementation)
TCP sockets for real-time communication (planned)
SignalR (planned for real-time communication)
Platform Targets
Desktop Host: Windows 10/11 with .NET 8.0 Runtime
Mobile Client:
Android 7.0+ (API level 24)
iOS 12.0+
macOS 10.15+ (Mac Catalyst)
Windows 10 version 1809+
Development Tools
Visual Studio 2022 17.8+ or Visual Studio Code with C# Dev Kit
.NET MAUI workload
Platform-specific SDKs (Android SDK, Xcode for iOS/macOS)
Project Structure
The solution follows a clean, modular architecture with shared libraries, platform-specific implementations, and comprehensive testing setup using the latest .NET 8.0 ecosystem and .NET MAUI for cross-platform mobile development.

RemoteLink - Bringing your desktop to any device, freely and securely.

