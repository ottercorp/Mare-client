using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Text;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using MareSynchronos.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationType = MareSynchronos.MareConfiguration.Models.NotificationType;

namespace MareSynchronos.Services;

public class NotificationService : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly INotificationManager _notificationManager;
    private readonly IChatGui _chatGui;
    private readonly MareConfigService _configurationService;
    private readonly ApiController _apiController;
    private readonly DalamudLinkPayload _pfinderChatLinkPayload;

    public NotificationService(ILogger<NotificationService> logger, MareMediator mediator,
        DalamudUtilService dalamudUtilService,
        INotificationManager notificationManager,
        ApiController apiController,
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui, MareConfigService configurationService) : base(logger, mediator)
    {
        _dalamudUtilService = dalamudUtilService;
        _notificationManager = notificationManager;
        _chatGui = chatGui;
        _configurationService = configurationService;
        _apiController = apiController;
        _pfinderChatLinkPayload = pluginInterface.AddChatLinkHandler(1, OnPfinderLinkClicked);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Mediator.Subscribe<NotificationMessage>(this, ShowNotification);
        Mediator.Subscribe<ConnectedMessage>(this, msg => { _ = SendLoginPfinderNoticeAsync(); });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void PrintErrorChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] Error: " + message);
        _chatGui.PrintError(se.BuiltString);
    }

    private void PrintInfoChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] Info: ").AddItalics(message ?? string.Empty);
        _chatGui.Print(se.BuiltString);
    }

    private void PrintWarnChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[Mare Synchronos] ").AddUiForeground("Warning: " + (message ?? string.Empty), 31).AddUiForegroundOff();
        _chatGui.Print(se.BuiltString);
    }

    private void ShowChat(NotificationMessage msg)
    {
        switch (msg.Type)
        {
            case NotificationType.Info:
                PrintInfoChat(msg.Message);
                break;

            case NotificationType.Warning:
                PrintWarnChat(msg.Message);
                break;

            case NotificationType.Error:
                PrintErrorChat(msg.Message);
                break;
        }
    }

    private void ShowNotification(NotificationMessage msg)
    {
        Logger.LogInformation("{msg}", msg.ToString());

        if (!_dalamudUtilService.IsLoggedIn) return;

        switch (msg.Type)
        {
            case NotificationType.Info:
                ShowNotificationLocationBased(msg, _configurationService.Current.InfoNotification);
                break;

            case NotificationType.Warning:
                ShowNotificationLocationBased(msg, _configurationService.Current.WarningNotification);
                break;

            case NotificationType.Error:
                ShowNotificationLocationBased(msg, _configurationService.Current.ErrorNotification);
                break;
            case NotificationType.Chat:
                PrintChat(msg);
                break;
        }
    }

    private async Task SendLoginPfinderNoticeAsync()
    {
        try
        {
            await Task.Delay(500).ConfigureAwait(false);

            int? count = null;
            try
            {
                if (_apiController.IsConnected)
                {
                    var list = await _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).ConfigureAwait(false);
                    count = list?.Count;
                }
            }
            catch (Exception ex)
            {
                Logger.LogTrace(ex, "PFinder count fetch failed at connect time");
            }
            if (!(count.HasValue && count.Value > 0)) return;
            var prefix = $"\uE044月海招募中心有{count.Value}条招募信息。 ";

            var message = new SeString(
                new TextPayload(prefix),
                _pfinderChatLinkPayload,
                new UIForegroundPayload(colors[_configurationService.Current.ChatColor]),
                new TextPayload("[ 打开招募中心 ]"),
                new UIForegroundPayload(0),
                RawPayload.LinkTerminator
            );

            _chatGui.Print(new XivChatEntry { Message = message, Type = XivChatType.SystemMessage });
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to send PFinder login notice");
        }
    }

    private void OnPfinderLinkClicked(uint cmdId, SeString msg)
    {
        try
        {
            Mediator.Publish(new UiToggleMessage(typeof(PFinderWindow)));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to open PFinder from chat link");
        }
    }

    private readonly ushort[] colors = new ushort[] { 1, 17, 25, 37, 43, 48, 524 };
    private void PrintChat(NotificationMessage msg)
    {
        _chatGui.Print(new SeStringBuilder().AddUiForeground(colors[_configurationService.Current.ChatColor]).AddText($"[{msg.Title}] {msg.Message}").AddUiForegroundOff().BuiltString);
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location)
    {
        switch (location)
        {
            case NotificationLocation.Toast:
                ShowToast(msg);
                break;

            case NotificationLocation.Chat:
                ShowChat(msg);
                break;

            case NotificationLocation.Both:
                ShowToast(msg);
                ShowChat(msg);
                break;

            case NotificationLocation.Nowhere:
                break;
        }
    }

    private void ShowToast(NotificationMessage msg)
    {
        Dalamud.Interface.ImGuiNotification.NotificationType dalamudType = msg.Type switch
        {
            NotificationType.Error => Dalamud.Interface.ImGuiNotification.NotificationType.Error,
            NotificationType.Warning => Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
            NotificationType.Info => Dalamud.Interface.ImGuiNotification.NotificationType.Info,
            _ => Dalamud.Interface.ImGuiNotification.NotificationType.Info
        };

        _notificationManager.AddNotification(new Notification()
        {
            Content = msg.Message ?? string.Empty,
            Title = msg.Title,
            Type = dalamudType,
            Minimized = false,
            InitialDuration = msg.TimeShownOnScreen ?? TimeSpan.FromSeconds(3)
        });
    }
}