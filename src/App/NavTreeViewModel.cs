using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

// Node types for the navigation tree
public abstract record NavTreeNode(string Label);
public record FolderNode(string Label, ObservableCollection<NavTreeNode> Children) : NavTreeNode(Label);
public record QueueNode(QueueInfo Info) : NavTreeNode(Info.Name);
public record TopicNode(TopicInfo Info) : NavTreeNode(Info.Name);
public record EventHubNode(EventHubInfo Info) : NavTreeNode(Info.Name);
public record RelayNode(RelayInfo Info) : NavTreeNode(Info.Name);
public record NotificationHubNode(NotificationHubInfo Info) : NavTreeNode(Info.Name);

public class NavTreeViewModel : ReactiveObject
{
    private readonly QueueListViewModel _queues;
    private readonly TopicListViewModel _topics;
    private readonly EventHubListViewModel _eventHubs;
    private readonly RelayListViewModel _relays;
    private readonly NotificationHubListViewModel _notifHubs;
    private readonly DashboardViewModel _dashboard;

    private NavTreeNode? _selectedNode;
    private ReactiveObject? _currentContent;
    private string _filterText = "";

    private readonly FolderNode _queuesFolder;
    private readonly FolderNode _topicsFolder;
    private readonly FolderNode _eventHubsFolder;
    private readonly FolderNode _relaysFolder;
    private readonly FolderNode _notifHubsFolder;
    private readonly FolderNode _dashboardFolder;

    public ObservableCollection<NavTreeNode> RootNodes { get; } = new();

    public NavTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNode, value);
            DispatchSelection(value);
        }
    }

    public ReactiveObject? CurrentContent
    {
        get => _currentContent;
        private set => this.RaiseAndSetIfChanged(ref _currentContent, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            this.RaiseAndSetIfChanged(ref _filterText, value);
            RebuildFilteredTree();
        }
    }

    // Context menu commands — folder level
    public ReactiveCommand<FolderNode, Unit> RefreshFolderCommand { get; private set; } = null!;
    public ReactiveCommand<FolderNode, Unit> CreateInFolderCommand { get; private set; } = null!;

    // Context menu commands — queue
    public ReactiveCommand<string, Unit> RefreshQueueCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> DeleteQueueCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> PurgeQueueCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> PurgeQueueDeadLetterCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> SendToQueueCommand { get; private set; } = null!;
    public ReactiveCommand<QueueNode, Unit> ChangeQueueStatusCommand { get; private set; } = null!;

    // Context menu commands — topic
    public ReactiveCommand<string, Unit> RefreshTopicCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> DeleteTopicCommand { get; private set; } = null!;
    public ReactiveCommand<TopicNode, Unit> ChangeTopicStatusCommand { get; private set; } = null!;

    // Context menu commands — relay / notification hub
    public ReactiveCommand<string, Unit> DeleteRelayCommand { get; private set; } = null!;
    public ReactiveCommand<string, Unit> DeleteNotifHubCommand { get; private set; } = null!;

    public NavTreeViewModel(MainViewModel mainVm)
    {
        _dashboard = mainVm.Dashboard;
        _queues = mainVm.Queues;
        _topics = mainVm.Topics;
        _eventHubs = mainVm.EventHubs;
        _relays = mainVm.Relays;
        _notifHubs = mainVm.NotificationHubs;

        _dashboardFolder = new FolderNode("📊 Dashboard", new ObservableCollection<NavTreeNode>());
        _queuesFolder = new FolderNode("📬 Queues", new ObservableCollection<NavTreeNode>());
        _topicsFolder = new FolderNode("📢 Topics", new ObservableCollection<NavTreeNode>());
        _eventHubsFolder = new FolderNode("⚡ Event Hubs", new ObservableCollection<NavTreeNode>());
        _relaysFolder = new FolderNode("🔗 Relays", new ObservableCollection<NavTreeNode>());
        _notifHubsFolder = new FolderNode("🔔 Notification Hubs", new ObservableCollection<NavTreeNode>());

        RootNodes.Add(_dashboardFolder);
        RootNodes.Add(_queuesFolder);
        RootNodes.Add(_topicsFolder);
        RootNodes.Add(_eventHubsFolder);
        RootNodes.Add(_relaysFolder);
        RootNodes.Add(_notifHubsFolder);

        // Observe collection changes and rebuild folder children
        ((INotifyCollectionChanged)_queues.Queues).CollectionChanged += (_, _) => RebuildQueueNodes();
        ((INotifyCollectionChanged)_topics.Topics).CollectionChanged += (_, _) => RebuildTopicNodes();
        ((INotifyCollectionChanged)_eventHubs.EventHubs).CollectionChanged += (_, _) => RebuildEventHubNodes();
        ((INotifyCollectionChanged)_relays.Relays).CollectionChanged += (_, _) => RebuildRelayNodes();
        ((INotifyCollectionChanged)_notifHubs.NotificationHubs).CollectionChanged += (_, _) => RebuildNotifHubNodes();

        // When SelectedDetail changes in a list VM, update CurrentContent
        Observable.Merge(
            _queues.WhenAnyValue(x => x.SelectedDetail).Select(_ => System.Reactive.Unit.Default),
            _topics.WhenAnyValue(x => x.SelectedDetail).Select(_ => System.Reactive.Unit.Default),
            _notifHubs.WhenAnyValue(x => x.SelectedDetail).Select(_ => System.Reactive.Unit.Default)
        ).Subscribe(_ => DispatchSelection(_selectedNode));

        // Initial population
        RebuildQueueNodes();
        RebuildTopicNodes();
        RebuildEventHubNodes();
        RebuildRelayNodes();
        RebuildNotifHubNodes();

        // Folder commands
        RefreshFolderCommand = ReactiveCommand.CreateFromTask<FolderNode>(async folder =>
        {
            if (folder == _queuesFolder) await _queues.RefreshCommand.Execute();
            else if (folder == _topicsFolder) await _topics.RefreshCommand.Execute();
            else if (folder == _eventHubsFolder) await _eventHubs.RefreshCommand.Execute();
            else if (folder == _relaysFolder) await _relays.RefreshCommand.Execute();
            else if (folder == _notifHubsFolder) await _notifHubs.RefreshCommand.Execute();
        });

        CreateInFolderCommand = ReactiveCommand.Create<FolderNode>(folder =>
        {
            if (folder == _queuesFolder) _queues.BeginCreateCommand.Execute().Subscribe();
            else if (folder == _topicsFolder) _topics.BeginCreateCommand.Execute().Subscribe();
        });

        // Queue commands
        RefreshQueueCommand = ReactiveCommand.CreateFromTask<string>(async name =>
        {
            _queues.SelectedQueue = _queues.Queues.FirstOrDefault(q => q.Name == name);
            if (_queues.SelectedDetail != null)
                await _queues.SelectedDetail.RefreshInfoCommand.Execute();
        });

        DeleteQueueCommand = ReactiveCommand.CreateFromTask<string>(async name =>
            await _queues.DeleteCommand.Execute(name));

        PurgeQueueCommand = ReactiveCommand.CreateFromTask<string>(async name =>
        {
            _queues.SelectedQueue = _queues.Queues.FirstOrDefault(q => q.Name == name);
            if (_queues.SelectedDetail != null)
                await _queues.SelectedDetail.PurgeCommand.Execute();
        });

        PurgeQueueDeadLetterCommand = ReactiveCommand.Create<string>(_ => { });

        SendToQueueCommand = ReactiveCommand.Create<string>(name =>
        {
            _queues.SelectedQueue = _queues.Queues.FirstOrDefault(q => q.Name == name);
            DispatchSelection(_selectedNode);
        });

        ChangeQueueStatusCommand = ReactiveCommand.Create<QueueNode>(_ => { });

        // Topic commands
        RefreshTopicCommand = ReactiveCommand.CreateFromTask<string>(async name =>
        {
            _topics.SelectedTopic = _topics.Topics.FirstOrDefault(t => t.Name == name);
            if (_topics.SelectedDetail != null)
                await _topics.SelectedDetail.RefreshInfoCommand.Execute();
        });

        DeleteTopicCommand = ReactiveCommand.CreateFromTask<string>(async name =>
            await _topics.DeleteCommand.Execute(name));

        ChangeTopicStatusCommand = ReactiveCommand.Create<TopicNode>(_ => { });

        // Relay / NotifHub commands
        DeleteRelayCommand = ReactiveCommand.Create<string>(_ => { });
        DeleteNotifHubCommand = ReactiveCommand.CreateFromTask<string>(async name =>
            await _notifHubs.DeleteCommand.Execute(name));

        // Default to Dashboard
        SelectedNode = _dashboardFolder;
    }

    private void RebuildQueueNodes()
    {
        RebuildFolderChildren(_queuesFolder,
            _queues.Queues.Where(MatchesFilter).Select(q => (NavTreeNode)new QueueNode(q)));
    }

    private void RebuildTopicNodes()
    {
        RebuildFolderChildren(_topicsFolder,
            _topics.Topics.Where(MatchesFilter).Select(t => (NavTreeNode)new TopicNode(t)));
    }

    private void RebuildEventHubNodes()
    {
        RebuildFolderChildren(_eventHubsFolder,
            _eventHubs.EventHubs.Where(MatchesFilter).Select(e => (NavTreeNode)new EventHubNode(e)));
    }

    private void RebuildRelayNodes()
    {
        RebuildFolderChildren(_relaysFolder,
            _relays.Relays.Where(MatchesFilter).Select(r => (NavTreeNode)new RelayNode(r)));
    }

    private void RebuildNotifHubNodes()
    {
        RebuildFolderChildren(_notifHubsFolder,
            _notifHubs.NotificationHubs.Where(MatchesFilter).Select(n => (NavTreeNode)new NotificationHubNode(n)));
    }

    private static void RebuildFolderChildren(FolderNode folder, IEnumerable<NavTreeNode> nodes)
    {
        folder.Children.Clear();
        foreach (var node in nodes)
            folder.Children.Add(node);
    }

    private bool MatchesFilter(QueueInfo q) => string.IsNullOrEmpty(_filterText) ||
        q.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    private bool MatchesFilter(TopicInfo t) => string.IsNullOrEmpty(_filterText) ||
        t.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    private bool MatchesFilter(EventHubInfo e) => string.IsNullOrEmpty(_filterText) ||
        e.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    private bool MatchesFilter(RelayInfo r) => string.IsNullOrEmpty(_filterText) ||
        r.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    private bool MatchesFilter(NotificationHubInfo n) => string.IsNullOrEmpty(_filterText) ||
        n.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);

    private void RebuildFilteredTree()
    {
        RebuildQueueNodes();
        RebuildTopicNodes();
        RebuildEventHubNodes();
        RebuildRelayNodes();
        RebuildNotifHubNodes();
    }

    private void DispatchSelection(NavTreeNode? node)
    {
        CurrentContent = node switch
        {
            null => _dashboard,
            FolderNode f when f == _dashboardFolder => _dashboard,
            FolderNode f when f == _queuesFolder => _queues,
            FolderNode f when f == _topicsFolder => _topics,
            FolderNode f when f == _eventHubsFolder => _eventHubs,
            FolderNode f when f == _relaysFolder => _relays,
            FolderNode f when f == _notifHubsFolder => _notifHubs,
            QueueNode q => SelectQueue(q.Info),
            TopicNode t => SelectTopic(t.Info),
            EventHubNode => _eventHubs.Detail,
            NotificationHubNode n => SelectNotifHub(n.Info),
            _ => _dashboard
        };
    }

    private ReactiveObject SelectQueue(QueueInfo info)
    {
        _queues.SelectedQueue = info;
        return _queues.SelectedDetail ?? (ReactiveObject)_queues;
    }

    private ReactiveObject SelectTopic(TopicInfo info)
    {
        _topics.SelectedTopic = info;
        return _topics.SelectedDetail ?? (ReactiveObject)_topics;
    }

    private ReactiveObject SelectNotifHub(NotificationHubInfo info)
    {
        _notifHubs.SelectedHub = info;
        return _notifHubs.SelectedDetail ?? (ReactiveObject)_notifHubs;
    }
}
