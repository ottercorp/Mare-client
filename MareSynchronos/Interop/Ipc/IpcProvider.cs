using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<IpcProvider> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly CharaDataManager _charaDataManager;
    private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;
    private ICallGateProvider<List<nint>>? _handledGameAddresses;
    private readonly List<GameObjectHandler> _activeGameObjectHandlers = [];

    private ICallGateProvider<object> _Ready;
    private ICallGateProvider<object> _Disposing;
    private ICallGateProvider<List<nint>>? _GetAllRendered;
    private ICallGateProvider<Dictionary<nint, (short, long, short, long)>>? _GetAllRenderedInfo;
    private ICallGateProvider<nint, (short, long, short, long)>? _GetAccessInfo;
    private ICallGateProvider<nint, object>? _PairRendered;
    private ICallGateProvider<nint, object>? _PairUnrendered;

    
    private ICallGateProvider<nint, string, bool, object?>? _ApplyToPairRequest;
    private ICallGateProvider<int, string, object?>? _moodlesShare;
    
    private readonly PairManager  _pairManager;
    private readonly IpcCallerChatTwo _chatTwoIpc;

    public MareMediator Mediator { get; init; }

    public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi,
        DalamudUtilService dalamudUtil, PairManager  pairManager,
        CharaDataManager charaDataManager, MareMediator mareMediator,
        ApiController apiController, MareConfigService mareConfigService,
        IpcCallerChatTwo chatTwoIpc)
    {
        _logger = logger;
        _pi = pi;
        _charaDataManager = charaDataManager;
        Mediator = mareMediator;
        _pairManager = pairManager;
        _chatTwoIpc = chatTwoIpc;
        // Initialize ChatTwo dependencies without retaining references here
        _chatTwoIpc.Initialize(mareConfigService, _pairManager, apiController);

        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Add(msg.GameObjectHandler);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Remove(msg.GameObjectHandler);
        });
        Mediator.Subscribe<RenderChangeMessage>(this, (msg) =>
        {
            if (msg.Visible)
            {
                _PairRendered?.SendMessage(msg.Address);
            }
            else
            {
                _PairUnrendered?.SendMessage(msg.Address);
            }
        });
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IpcProviderService");
        _loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("MareSynchronos.LoadMcdf");
        _loadFileProvider.RegisterFunc(LoadMcdf);
        _loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("MareSynchronos.LoadMcdfAsync");
        _loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
        _handledGameAddresses = _pi.GetIpcProvider<List<nint>>("MareSynchronos.GetHandledAddresses");
        _handledGameAddresses.RegisterFunc(GetHandledAddresses);

        _ApplyToPairRequest = _pi.GetIpcProvider<nint, string, bool, object?>("Sundouleia.ApplyToPairRequest");
        _ApplyToPairRequest.RegisterAction(HandleApplyStatusesToPairRequest);
        _moodlesShare = _pi.GetIpcProvider<int, string, object?>("MareSynchronos.MoodlesShare");
        _moodlesShare.RegisterAction(ShareMoodles);

        // Register ChatTwo IPC providers
        _chatTwoIpc.RegisterProviders();
        
        _Ready = _pi.GetIpcProvider<object>("Sundouleia.Ready");
        _Ready?.SendMessage();
        _Disposing = _pi.GetIpcProvider<object>("Sundouleia.Disposing");
        _GetAllRendered = _pi.GetIpcProvider<List<nint>>("Sundouleia.GetAllRendered");
        _GetAllRendered.RegisterFunc(GetHandledAddresses);
        _GetAllRenderedInfo = _pi.GetIpcProvider<Dictionary<nint, (short, long, short, long)>>("Sundouleia.GetAllRenderedInfo");
        _GetAllRenderedInfo.RegisterFunc(GetAllRenderedInfo);
        _GetAccessInfo = _pi.GetIpcProvider<nint, (short, long, short, long)>("Sundouleia.GetAccessInfo");
        _GetAccessInfo.RegisterFunc(GetAccessInfo);
        _PairRendered = _pi.GetIpcProvider<nint, object>("Sundouleia.PairRendered");
        _PairUnrendered =  _pi.GetIpcProvider<nint, object>("Sundouleia.PairUnrendered");
        

        _logger.LogInformation("Started IpcProviderService");
        return Task.CompletedTask;
    }

    private (short, long, short, long) GetAccessInfo(IntPtr arg)
    {
        return ((short)0b11111111, (long)TimeSpan.MaxValue.TotalMilliseconds, (short)0b11111111, (long)TimeSpan.MaxValue.TotalMilliseconds);
    }

    private Dictionary<nint, (short, long, short, long)> GetAllRenderedInfo()
    {
        return _activeGameObjectHandlers.Where(g => g.Address != nint.Zero && g.ObjectKind == ObjectKind.Player).Distinct()
            .Select(x => (x.Address, ((short)0b11111111, (long)TimeSpan.MaxValue.TotalMilliseconds, (short)0b11111111, (long)TimeSpan.MaxValue.TotalMilliseconds)))
            .ToDictionary();
    }

    private void ShareMoodles(int action, string status)
    {
        var msg = new MoodlesShareMessage((MoodlesAction)action, status);
        Mediator.Publish(msg);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        _loadFileProvider?.UnregisterFunc();
        _loadFileAsyncProvider?.UnregisterFunc();
        _handledGameAddresses?.UnregisterFunc();
        _ApplyToPairRequest?.UnregisterAction();

        // Unregister ChatTwo IPC providers
        _chatTwoIpc.UnregisterProviders();
        
        _GetAllRendered?.UnregisterFunc();
        _GetAllRenderedInfo?.UnregisterFunc();
        _GetAccessInfo?.UnregisterFunc();
        _Disposing?.SendMessage();
        

        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private async Task<bool> LoadMcdfAsync(string path, IGameObject target)
    {
        await ApplyFileAsync(path, target).ConfigureAwait(false);

        return true;
    }

    private bool LoadMcdf(string path, IGameObject target)
    {
        _ = Task.Run(async () => await ApplyFileAsync(path, target).ConfigureAwait(false)).ConfigureAwait(false);

        return true;
    }

    private async Task ApplyFileAsync(string path, IGameObject target)
    {
        _charaDataManager.LoadMcdf(path);
        await (_charaDataManager.LoadedMcdfHeader ?? Task.CompletedTask).ConfigureAwait(false);
        _charaDataManager.McdfApplyToTarget(target.Name.TextValue);
    }

    private List<nint> GetHandledAddresses()
    {
        return _activeGameObjectHandlers.Where(g => g.Address != nint.Zero).Select(g => g.Address).Distinct().ToList();
    }

        /// <summary>
    /// Handles the request from our clients moodles plugin to update another one of our pairs status.
    /// </summary>
    /// <param name="requester">The name of the player requesting the apply (SHOULD ALWAYS BE OUR CLIENT PLAYER) </param>
    /// <param name="recipient">The name of the player to apply the status to. (SHOULD ALWAYS BE A PAIR) </param>
    /// <param name="statuses">The list of statuses to apply to the recipient. </param>
    private void HandleApplyStatusesToPairRequest(nint address, string statuses, bool preset)
    {
        try
        {
            var name = _activeGameObjectHandlers.FirstOrDefault(g => g.Address == address)?.Name;
            if (name == null)
            {
                _logger.LogWarning("Received ApplyStatusesToPairRequest for {recipient} but could not find the UID for the pair", name);
                return;
            }
            var pairUser = _pairManager.GetOnlineUserPairs().Find(p => p.PlayerName == name && p.IsVisible)?.UserData;
            if (pairUser == null)
            {
                _logger.LogWarning("Received ApplyStatusesToPairRequest for {recipient} but could not find the pair", name);
                return;
            }
            // fetch the UID for the pair to apply for.
            _logger.LogDebug("Received ApplyStatusesToPair request to {recipient}, applying statuses", name);
            var dto = new ApplyMoodlesByStatusDto(pairUser, statuses);
            Mediator.Publish(new MoodlesApplyStatusToPair(dto));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failure handling ApplyStatusesToPairRequest: ");
            throw;
        }

    }
}