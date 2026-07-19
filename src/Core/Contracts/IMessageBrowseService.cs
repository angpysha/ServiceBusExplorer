#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Non-destructive, bounded message browse port.
/// </summary>
public interface IMessageBrowseService
{
  /// <summary>
  /// Peeks messages from the explicit source without removing or locking them.
  /// </summary>
  Task<MessageBrowseResult> PeekAsync(
      EntityAddress address,
      MessageSource source,
      PageRequest page,
      CancellationToken cancellationToken = default);
}
