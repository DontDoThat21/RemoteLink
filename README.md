# RemoteLink - Free Remote Desktop Solution

A free, open-source remote desktop application that allows you to remotely access your Windows desktop from an Android mobile device. RemoteLink provides a cost-effective, locally-hosted alternative to TeamViewer with peer-to-peer connectivity and no cloud dependencies.

## 🚀 Features

- **Cross-Platform Support**: Windows desktop host and Android mobile client
- **Local Network Discovery**: Automatic discovery of devices on the local network
- **Real-Time Screen Sharing**: Live desktop screen streaming to mobile device
- **Remote Input Control**: Mouse and keyboard input from mobile to desktop
- **Secure Communication**: Encrypted peer-to-peer connections
- **No Cloud Dependencies**: Works entirely on local network
- **Free & Open Source**: No subscription fees or licensing costs

## 📋 Project Status

This project is currently in **initial development phase**. The following core components have been implemented:

### ✅ Completed Features
- [x] Basic project structure and architecture
- [x] Network discovery service (UDP-based)
- [x] Core data models and interfaces
- [x] Desktop host application foundation
- [x] Mobile client application foundation
- [x] Cross-platform compatibility layer
- [x] Dependency injection and logging setup

### 🚧 In Progress
- [ ] Real-time communication service (TCP/SignalR)
- [ ] Screen capture implementation (Windows-specific)
- [ ] Input handling (Windows API integration)
- [ ] Mobile UI for remote control
- [ ] Authentication and pairing mechanism
- [ ] Performance optimization

### 📋 Planned Features
- [ ] End-to-end encryption
- [ ] Multi-monitor support
- [ ] File transfer capability
- [ ] Connection quality optimization
- [ ] Audio streaming
- [ ] Session recording

## 🏗️ Architecture

```
RemoteLink Solution
├── src/
│   ├── RemoteLink.Shared/          # Common interfaces, models, and services
│   ├── RemoteLink.Desktop/         # Windows desktop host application
│   └── RemoteLink.Mobile/          # Android mobile client application
├── tests/
│   ├── RemoteLink.Shared.Tests/    # Unit tests for shared components
│   └── RemoteLink.Desktop.Tests/   # Unit tests for desktop application
└── docs/                           # Documentation and guides
```

### Core Components

1. **Shared Library (`RemoteLink.Shared`)**
   - Network discovery service
   - Communication interfaces
   - Data models (DeviceInfo, ScreenData, InputEvent)
   - Cross-platform services

2. **Desktop Host (`RemoteLink.Desktop`)**
   - Screen capture service
   - Input handling service
   - Network broadcasting
   - Windows-specific implementations

3. **Mobile Client (`RemoteLink.Mobile`)**
   - Device discovery and connection
   - Remote input generation
   - Screen display and interaction
   - Touch-to-mouse translation

## 🔧 Technical Requirements

### Development Environment
- .NET 8.0 SDK or later
- Visual Studio 2022 or Visual Studio Code
- Windows 10/11 (for desktop host development)
- Android SDK (for mobile client development)

### Runtime Requirements
- **Desktop Host**: Windows 10/11 with .NET 8.0 Runtime
- **Mobile Client**: Android 7.0 (API level 24) or later
- **Network**: Local network connectivity between devices

## 🚀 Getting Started

### Prerequisites

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
2. Clone this repository:
   ```bash
   git clone https://github.com/DontDoThat21/RemoteLink.git
   cd RemoteLink
   ```

### Building the Solution

```bash
# Restore dependencies and build all projects
dotnet restore
dotnet build

# Run tests
dotnet test
```

### Running the Desktop Host

```bash
# Navigate to desktop project
cd src/RemoteLink.Desktop/RemoteLink.Desktop

# Run the desktop host
dotnet run
```

### Running the Mobile Client

```bash
# Navigate to mobile project  
cd src/RemoteLink.Mobile/RemoteLink.Mobile

# Run the mobile client
dotnet run
```

## 🔧 Configuration

### Network Settings

- **Discovery Port**: 12345 (UDP)
- **Desktop Host Port**: 12346 (TCP)
- **Mobile Client Port**: 12347 (TCP)
- **Broadcast Interval**: 5 seconds
- **Device Timeout**: 15 seconds

### Performance Settings

- **Screen Capture Rate**: 10 FPS (configurable)
- **Image Quality**: 75% JPEG compression (configurable)
- **Default Resolution**: 1920x1080 (auto-detected)

## 🧪 Testing

The project includes comprehensive unit tests for core functionality:

```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/RemoteLink.Shared.Tests/
```

## 🤝 Contributing

We welcome contributions to RemoteLink! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

### Development Workflow

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/new-feature`
3. Make your changes and add tests
4. Ensure all tests pass: `dotnet test`
5. Commit your changes: `git commit -m 'Add new feature'`
6. Push to the branch: `git push origin feature/new-feature`
7. Submit a pull request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

- **Issues**: [GitHub Issues](https://github.com/DontDoThat21/RemoteLink/issues)
- **Discussions**: [GitHub Discussions](https://github.com/DontDoThat21/RemoteLink/discussions)
- **Documentation**: [Wiki](https://github.com/DontDoThat21/RemoteLink/wiki)

## 🗺️ Roadmap

### Phase 1: Core Foundation (Current)
- Basic project structure ✅
- Network discovery ✅ 
- Core interfaces and models ✅

### Phase 2: Basic Functionality (Next)
- Real-time communication
- Screen capture and streaming
- Basic input handling

### Phase 3: Enhanced Features
- Mobile UI development
- Authentication and security
- Performance optimization

### Phase 4: Advanced Features
- Multi-monitor support
- File transfer
- Audio streaming
- Advanced security features

## 🙏 Acknowledgments

- Thanks to all contributors and testers
- Inspired by open-source remote desktop solutions
- Built with modern .NET technologies

---

**RemoteLink** - Bringing your desktop to your mobile device, freely and securely.