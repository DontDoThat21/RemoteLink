using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Interfaces;

/// <summary>
/// Manages persistent history of past remote connection sessions.
/// </summary>
public interface IConnectionHistoryService
{
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ConnectionRecord> GetAll();
    Task AddAsync(ConnectionRecord record, CancellationToken cancellationToken = default);
    IReadOnlyList<ConnectionRecord> GetRecent(int count);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
