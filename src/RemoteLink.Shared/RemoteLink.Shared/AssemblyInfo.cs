using System.Runtime.CompilerServices;

// Allow the Desktop test project to access internal members (e.g. SessionManager.UtcNow,
// RemoteSession.ClockFunc, RemoteSession._accumulatedDuration) for white-box testing.
[assembly: InternalsVisibleTo("RemoteLink.Desktop.Tests")]
