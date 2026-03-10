using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// JSON-backed local account store with secure password hashing and persistent login sessions.
/// </summary>
public sealed class UserAccountService : IUserAccountService
{
    private const int HashIterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _accountsPath;
    private readonly string _sessionPath;
    private readonly ILogger<UserAccountService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private bool _loaded;
    private List<PersistedUserAccount> _accounts = new();
    private UserAccountSession? _currentSession;

    public UserAccountService(ILogger<UserAccountService> logger, string? storageDirectory = null)
    {
        _logger = logger;
        var root = storageDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            root = Path.Combine(appData, "RemoteLink");
        }

        _accountsPath = Path.Combine(root, "user_accounts.json");
        _sessionPath = Path.Combine(root, "user_session.json");
    }

    public UserAccountSession? CurrentSession => _currentSession is null ? null : CloneSession(_currentSession);

    public bool IsSignedIn => _currentSession is not null;

    public event EventHandler<UserAccountSession?>? SessionChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        UserAccountSession? session;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            session = _currentSession is null ? null : CloneSession(_currentSession);
        }
        finally
        {
            _lock.Release();
        }

        SessionChanged?.Invoke(this, session);
    }

    public async Task<UserAccountSession> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        ValidatePassword(password);

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));

        UserAccountSession session;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);

            if (_accounts.Any(account => string.Equals(account.Email, normalizedEmail, StringComparison.Ordinal)))
                throw new InvalidOperationException("An account with this email already exists.");

            var (hash, salt) = HashPassword(password);
            var account = new PersistedUserAccount
            {
                AccountId = Guid.NewGuid().ToString("N"),
                Email = normalizedEmail,
                DisplayName = displayName.Trim(),
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAtUtc = DateTime.UtcNow,
                LastLoginAtUtc = DateTime.UtcNow
            };

            _accounts.Add(account);
            session = CreateSession(account);
            _currentSession = CloneSession(session);

            await SaveAccountsInternalAsync(cancellationToken).ConfigureAwait(false);
            await SaveSessionInternalAsync(session, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Registered account {Email}", normalizedEmail);
        }
        finally
        {
            _lock.Release();
        }

        SessionChanged?.Invoke(this, CloneSession(session));
        return session;
    }

    public async Task<UserAccountSession> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        ValidatePassword(password);

        UserAccountSession session;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);

            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(candidate.Email, normalizedEmail, StringComparison.Ordinal));

            if (account is null || !VerifyPassword(password, account.PasswordHash, account.PasswordSalt))
                throw new UnauthorizedAccessException("Invalid email or password.");

            account.LastLoginAtUtc = DateTime.UtcNow;
            session = CreateSession(account);
            _currentSession = CloneSession(session);

            await SaveAccountsInternalAsync(cancellationToken).ConfigureAwait(false);
            await SaveSessionInternalAsync(session, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Signed in account {Email}", normalizedEmail);
        }
        finally
        {
            _lock.Release();
        }

        SessionChanged?.Invoke(this, CloneSession(session));
        return session;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);

            _currentSession = null;
            if (File.Exists(_sessionPath))
                File.Delete(_sessionPath);

            _logger.LogInformation("Signed out current account session");
        }
        finally
        {
            _lock.Release();
        }

        SessionChanged?.Invoke(this, null);
    }

    public async Task<UserAccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            var account = GetCurrentAccountOrNull();
            return account is null ? null : MapProfile(account);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RegisterDeviceAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            var account = GetCurrentAccountOrThrow();
            var mappedDevice = MapManagedDevice(device);
            var existing = account.ManagedDevices.FirstOrDefault(candidate => DevicesMatch(candidate, mappedDevice));

            if (existing is null)
            {
                account.ManagedDevices.Add(mappedDevice);
            }
            else
            {
                existing.DeviceId = mappedDevice.DeviceId;
                existing.InternetDeviceId = mappedDevice.InternetDeviceId;
                existing.DeviceName = mappedDevice.DeviceName;
                existing.Type = mappedDevice.Type;
                existing.IPAddress = mappedDevice.IPAddress;
                existing.Port = mappedDevice.Port;
                existing.SupportsRelay = mappedDevice.SupportsRelay;
                existing.RelayServerHost = mappedDevice.RelayServerHost;
                existing.RelayServerPort = mappedDevice.RelayServerPort;
                existing.LastSeenAtUtc = mappedDevice.LastSeenAtUtc;
            }

            await SaveAccountsInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveManagedDeviceAsync(string deviceIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentifier))
            throw new ArgumentException("Device identifier is required.", nameof(deviceIdentifier));

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            var account = GetCurrentAccountOrThrow();
            var normalizedInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(deviceIdentifier);

            account.ManagedDevices.RemoveAll(device =>
                string.Equals(device.DeviceId, deviceIdentifier, StringComparison.OrdinalIgnoreCase) ||
                (normalizedInternetId is not null &&
                 string.Equals(DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId), normalizedInternetId, StringComparison.Ordinal)));

            await SaveAccountsInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AccountManagedDevice>> GetManagedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            var account = GetCurrentAccountOrThrow();
            return account.ManagedDevices
                .OrderByDescending(device => device.LastSeenAtUtc)
                .Select(CloneManagedDevice)
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SyncSavedDevicesAsync(IEnumerable<SavedDevice> savedDevices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(savedDevices);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            var account = GetCurrentAccountOrThrow();
            var snapshot = new List<SavedDevice>();

            foreach (var device in savedDevices)
            {
                var clone = CloneSavedDevice(device);
                clone.InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(clone.InternetDeviceId);
                var existing = snapshot.FirstOrDefault(candidate => SavedDevicesMatch(candidate, clone));

                if (existing is null)
                {
                    snapshot.Add(clone);
                    continue;
                }

                existing.FriendlyName = clone.FriendlyName;
                existing.DeviceName = clone.DeviceName;
                existing.DeviceId = string.IsNullOrWhiteSpace(clone.DeviceId) ? existing.DeviceId : clone.DeviceId;
                existing.InternetDeviceId = clone.InternetDeviceId ?? existing.InternetDeviceId;
                existing.IPAddress = clone.IPAddress;
                existing.Port = clone.Port;
                existing.SupportsRelay = clone.SupportsRelay;
                existing.RelayServerHost = clone.RelayServerHost;
                existing.RelayServerPort = clone.RelayServerPort;
                existing.Type = clone.Type;
                existing.LastConnected = clone.LastConnected;
                existing.DateAdded = clone.DateAdded;
            }

            account.SyncedSavedDevices = snapshot;
            await SaveAccountsInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SavedDevice>> GetSyncedSavedDevicesAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            var account = GetCurrentAccountOrThrow();
            return account.SyncedSavedDevices
                .OrderByDescending(device => device.LastConnected ?? DateTime.MinValue)
                .ThenBy(device => device.FriendlyName)
                .Select(CloneSavedDevice)
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;

        if (File.Exists(_accountsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_accountsPath, cancellationToken).ConfigureAwait(false);
                _accounts = JsonSerializer.Deserialize<List<PersistedUserAccount>>(json, JsonOptions) ?? new List<PersistedUserAccount>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user accounts from {Path}", _accountsPath);
                _accounts = new List<PersistedUserAccount>();
            }
        }

        if (File.Exists(_sessionPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_sessionPath, cancellationToken).ConfigureAwait(false);
                var persistedSession = JsonSerializer.Deserialize<PersistedSession>(json, JsonOptions);
                var account = persistedSession is null
                    ? null
                    : _accounts.FirstOrDefault(candidate => candidate.AccountId == persistedSession.AccountId);

                if (persistedSession is not null && account is not null && persistedSession.ExpiresAtUtc > DateTime.UtcNow)
                {
                    _currentSession = new UserAccountSession
                    {
                        AccountId = account.AccountId,
                        Email = account.Email,
                        DisplayName = account.DisplayName,
                        SessionToken = persistedSession.SessionToken,
                        CreatedAtUtc = persistedSession.CreatedAtUtc,
                        ExpiresAtUtc = persistedSession.ExpiresAtUtc
                    };
                }
                else
                {
                    _currentSession = null;
                    File.Delete(_sessionPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user session from {Path}", _sessionPath);
                _currentSession = null;
            }
        }

        _loaded = true;
    }

    private async Task SaveAccountsInternalAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_accountsPath)!);
        var json = JsonSerializer.Serialize(_accounts, JsonOptions);
        await File.WriteAllTextAsync(_accountsPath, json, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveSessionInternalAsync(UserAccountSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        var persistedSession = new PersistedSession
        {
            AccountId = session.AccountId,
            SessionToken = session.SessionToken,
            CreatedAtUtc = session.CreatedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc
        };

        var json = JsonSerializer.Serialize(persistedSession, JsonOptions);
        await File.WriteAllTextAsync(_sessionPath, json, cancellationToken).ConfigureAwait(false);
    }

    private PersistedUserAccount GetCurrentAccountOrThrow()
    {
        return GetCurrentAccountOrNull() ?? throw new InvalidOperationException("No user account is currently signed in.");
    }

    private PersistedUserAccount? GetCurrentAccountOrNull()
    {
        return _currentSession is null
            ? null
            : _accounts.FirstOrDefault(account => account.AccountId == _currentSession.AccountId);
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        var trimmed = email.Trim();
        try
        {
            _ = new MailAddress(trimmed);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("A valid email address is required.", nameof(email), ex);
        }

        return trimmed.ToLowerInvariant();
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.", nameof(password));
    }

    private static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, HashIterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    private static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var salt = Convert.FromBase64String(storedSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, HashIterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(storedHash));
    }

    private static UserAccountSession CreateSession(PersistedUserAccount account)
    {
        return new UserAccountSession
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            SessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
        };
    }

    private static UserAccountProfile MapProfile(PersistedUserAccount account)
    {
        return new UserAccountProfile
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            CreatedAtUtc = account.CreatedAtUtc,
            LastLoginAtUtc = account.LastLoginAtUtc,
            ManagedDevices = account.ManagedDevices.Select(CloneManagedDevice).ToList(),
            SyncedSavedDevices = account.SyncedSavedDevices.Select(CloneSavedDevice).ToList()
        };
    }

    private static AccountManagedDevice MapManagedDevice(DeviceInfo device)
    {
        return new AccountManagedDevice
        {
            DeviceId = device.DeviceId,
            InternetDeviceId = DeviceIdentityManager.NormalizeInternetDeviceId(device.InternetDeviceId),
            DeviceName = device.DeviceName,
            Type = device.Type,
            IPAddress = device.IPAddress,
            Port = device.Port,
            SupportsRelay = device.SupportsRelay,
            RelayServerHost = device.RelayServerHost,
            RelayServerPort = device.RelayServerPort,
            LastSeenAtUtc = DateTime.UtcNow
        };
    }

    private static bool DevicesMatch(AccountManagedDevice left, AccountManagedDevice right)
    {
        var leftInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(left.InternetDeviceId);
        var rightInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(right.InternetDeviceId);

        return (!string.IsNullOrWhiteSpace(left.DeviceId) &&
                !string.IsNullOrWhiteSpace(right.DeviceId) &&
                string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
               (leftInternetId is not null &&
                rightInternetId is not null &&
                string.Equals(leftInternetId, rightInternetId, StringComparison.Ordinal));
    }

    private static bool SavedDevicesMatch(SavedDevice left, SavedDevice right)
    {
        var leftInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(left.InternetDeviceId);
        var rightInternetId = DeviceIdentityManager.NormalizeInternetDeviceId(right.InternetDeviceId);

        return (!string.IsNullOrWhiteSpace(left.DeviceId) &&
                !string.IsNullOrWhiteSpace(right.DeviceId) &&
                string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)) ||
               (leftInternetId is not null &&
                rightInternetId is not null &&
                string.Equals(leftInternetId, rightInternetId, StringComparison.Ordinal));
    }

    private static UserAccountSession CloneSession(UserAccountSession session)
    {
        return new UserAccountSession
        {
            AccountId = session.AccountId,
            Email = session.Email,
            DisplayName = session.DisplayName,
            SessionToken = session.SessionToken,
            CreatedAtUtc = session.CreatedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc
        };
    }

    private static AccountManagedDevice CloneManagedDevice(AccountManagedDevice device)
    {
        return new AccountManagedDevice
        {
            DeviceId = device.DeviceId,
            InternetDeviceId = device.InternetDeviceId,
            DeviceName = device.DeviceName,
            Type = device.Type,
            IPAddress = device.IPAddress,
            Port = device.Port,
            SupportsRelay = device.SupportsRelay,
            RelayServerHost = device.RelayServerHost,
            RelayServerPort = device.RelayServerPort,
            LastSeenAtUtc = device.LastSeenAtUtc
        };
    }

    private static SavedDevice CloneSavedDevice(SavedDevice device)
    {
        return new SavedDevice
        {
            Id = device.Id,
            FriendlyName = device.FriendlyName,
            DeviceName = device.DeviceName,
            DeviceId = device.DeviceId,
            InternetDeviceId = device.InternetDeviceId,
            IPAddress = device.IPAddress,
            Port = device.Port,
            SupportsRelay = device.SupportsRelay,
            RelayServerHost = device.RelayServerHost,
            RelayServerPort = device.RelayServerPort,
            Type = device.Type,
            LastConnected = device.LastConnected,
            DateAdded = device.DateAdded
        };
    }

    private sealed class PersistedUserAccount
    {
        public string AccountId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastLoginAtUtc { get; set; }
        public List<AccountManagedDevice> ManagedDevices { get; set; } = new();
        public List<SavedDevice> SyncedSavedDevices { get; set; } = new();
    }

    private sealed class PersistedSession
    {
        public string AccountId { get; set; } = string.Empty;
        public string SessionToken { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
