using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class SubscriptionListViewModel : ReactiveObject
{
    private readonly ISubscriptionService _svc;
    private readonly string _topicName;
    private readonly SourceList<SubscriptionInfo> _source = new();
    private bool _isLoading;
    private string? _error;
    private SubscriptionInfo? _selectedSubscription;
    private bool _isCreating;
    private string _newSubscriptionName = "";

    public ReadOnlyObservableCollection<SubscriptionInfo> Subscriptions { get; }

    public SubscriptionInfo? SelectedSubscription
    {
        get => _selectedSubscription;
        set => this.RaiseAndSetIfChanged(ref _selectedSubscription, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string? Error
    {
        get => _error;
        private set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        set => this.RaiseAndSetIfChanged(ref _isCreating, value);
    }

    public string NewSubscriptionName
    {
        get => _newSubscriptionName;
        set => this.RaiseAndSetIfChanged(ref _newSubscriptionName, value);
    }

    public ReactiveCommand<Unit, IReadOnlyList<SubscriptionInfo>> RefreshCommand { get; }
    public ReactiveCommand<CreateSubscriptionOptions, SubscriptionInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> QuickCreateCommand { get; }

    public SubscriptionListViewModel(ISubscriptionService svc, string topicName)
    {
        _svc = svc;
        _topicName = topicName;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Subscriptions = bound;

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                var items = await _svc.ListAsync(_topicName);
                _source.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(items);
                });
                return items;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return (IReadOnlyList<SubscriptionInfo>)Array.Empty<SubscriptionInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        });

        CreateCommand = ReactiveCommand.CreateFromTask<CreateSubscriptionOptions, SubscriptionInfo>(async opts =>
        {
            var created = await _svc.CreateAsync(opts);
            _source.Add(created);
            return created;
        });

        DeleteCommand = ReactiveCommand.CreateFromTask<string, Unit>(async name =>
        {
            await _svc.DeleteAsync(_topicName, name);
            _source.Edit(list =>
            {
                var item = list.FirstOrDefault(s => s.Name == name);
                if (item != null) list.Remove(item);
            });
            return Unit.Default;
        });

        BeginCreateCommand = ReactiveCommand.Create(() => { IsCreating = true; });
        CancelCreateCommand = ReactiveCommand.Create(() =>
        {
            IsCreating = false;
            NewSubscriptionName = "";
        });

        var canQuickCreate = this.WhenAnyValue(x => x.NewSubscriptionName,
            n => !string.IsNullOrWhiteSpace(n));
        QuickCreateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var created = await _svc.CreateAsync(new CreateSubscriptionOptions(_topicName, NewSubscriptionName));
            _source.Add(created);
            IsCreating = false;
            NewSubscriptionName = "";
        }, canQuickCreate);
    }
}
