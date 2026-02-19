namespace RemoteLink.Shared.Interfaces;

using RemoteLink.Shared.Models;

/// <summary>
/// Service for managing file transfers between client and host.
/// </summary>
public interface IFileTransferService
{
    /// <summary>
    /// Initiates a file transfer (upload or download).
    /// </summary>
    /// <param name="filePath">Local path to the file.</param>
    /// <param name="direction">Transfer direction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transfer ID on success.</returns>
    Task<string> InitiateTransferAsync(string filePath, FileTransferDirection direction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts an incoming file transfer request.
    /// </summary>
    /// <param name="transferId">Transfer ID from the request.</param>
    /// <param name="savePath">Local path where file will be saved.</param>
    /// <returns>True if accepted successfully.</returns>
    Task<bool> AcceptTransferAsync(string transferId, string savePath);

    /// <summary>
    /// Rejects an incoming file transfer request.
    /// </summary>
    /// <param name="transferId">Transfer ID.</param>
    /// <param name="reason">Reason for rejection.</param>
    /// <returns>True if rejected successfully.</returns>
    Task<bool> RejectTransferAsync(string transferId, FileTransferRejectionReason reason);

    /// <summary>
    /// Cancels an ongoing transfer.
    /// </summary>
    /// <param name="transferId">Transfer ID.</param>
    /// <returns>True if cancelled successfully.</returns>
    Task<bool> CancelTransferAsync(string transferId);

    /// <summary>
    /// Gets progress for a transfer.
    /// </summary>
    /// <param name="transferId">Transfer ID.</param>
    /// <returns>Progress information, or null if not found.</returns>
    FileTransferProgress? GetProgress(string transferId);

    /// <summary>
    /// Gets all active transfers.
    /// </summary>
    /// <returns>List of transfer IDs.</returns>
    IReadOnlyList<string> GetActiveTransfers();

    /// <summary>
    /// Fired when a new transfer request is received.
    /// </summary>
    event EventHandler<FileTransferRequest>? TransferRequested;

    /// <summary>
    /// Fired when a transfer response is received.
    /// </summary>
    event EventHandler<FileTransferResponse>? TransferResponseReceived;

    /// <summary>
    /// Fired when transfer progress updates.
    /// </summary>
    event EventHandler<FileTransferProgress>? ProgressUpdated;

    /// <summary>
    /// Fired when a transfer completes (success or failure).
    /// </summary>
    event EventHandler<FileTransferComplete>? TransferCompleted;

    /// <summary>
    /// Fired when a chunk is received during an active transfer.
    /// </summary>
    event EventHandler<FileTransferChunk>? ChunkReceived;
}
