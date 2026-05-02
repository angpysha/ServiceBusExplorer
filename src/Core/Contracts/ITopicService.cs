#nullable enable
namespace ServiceBusExplorer;

public interface ITopicService
{
    Task<IReadOnlyList<TopicInfo>> ListAsync(CancellationToken ct = default);
    Task<TopicInfo> GetAsync(string name, CancellationToken ct = default);
    Task<TopicInfo> CreateAsync(CreateTopicOptions opts, CancellationToken ct = default);
    Task<TopicInfo> UpdateAsync(TopicInfo updated, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
