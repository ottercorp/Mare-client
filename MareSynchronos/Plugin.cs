﻿using Dalamud.ContextMenu;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.PlayerData.Services;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Components.Popup;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos;

public sealed class Plugin : IDalamudPlugin
{
    private readonly CancellationTokenSource _pluginCts = new();
    private readonly Task _hostBuilderRunTask;

    public Plugin(DalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData,
        IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui,
        IGameGui gameGui, IDtrBar dtrBar, IPluginLog pluginLog, ITargetManager targetManager, INotificationManager notificationManager)
    {
        _hostBuilderRunTask = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging(pluginLog);
            lb.SetMinimumLevel(LogLevel.Trace);
        })
        .ConfigureServices(collection =>
        {
            collection.AddSingleton(new WindowSystem("MareSynchronos"));
            collection.AddSingleton<FileDialogManager>();
            collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));

            // add mare related singletons
            collection.AddSingleton<MareMediator>();
            collection.AddSingleton<FileCacheManager>();
            collection.AddSingleton<ServerConfigurationManager>();
            collection.AddSingleton<PairManager>();
            collection.AddSingleton<ApiController>();
            collection.AddSingleton<MareCharaFileManager>();
            collection.AddSingleton<PerformanceCollectorService>();
            collection.AddSingleton<HubFactory>();
            collection.AddSingleton<FileUploadManager>();
            collection.AddSingleton<FileTransferOrchestrator>();
            collection.AddSingleton<MarePlugin>();
            collection.AddSingleton<MareProfileManager>();
            collection.AddSingleton<GameObjectHandlerFactory>();
            collection.AddSingleton<FileDownloadManagerFactory>();
            collection.AddSingleton<PairHandlerFactory>();
            collection.AddSingleton<PairFactory>();
            collection.AddSingleton<ModelAnalyzer>(s => new(s.GetRequiredService<ILogger<ModelAnalyzer>>(), s.GetRequiredService<FileCacheManager>(),
                s.GetRequiredService<TriangleCalculationConfigService>(), gameData));
            collection.AddSingleton<CharacterAnalyzer>();
            collection.AddSingleton<TokenProvider>();
            collection.AddSingleton<PluginWarningNotificationService>();
            collection.AddSingleton<FileCompactor>();
            collection.AddSingleton<TagHandler>();
            collection.AddSingleton<IdDisplayHandler>();
            collection.AddSingleton((s) => new IpcProvider(s.GetRequiredService<ILogger<IpcProvider>>(),
                pluginInterface,
                s.GetRequiredService<MareCharaFileManager>(), s.GetRequiredService<DalamudUtilService>(),
                s.GetRequiredService<MareMediator>()));
            collection.AddSingleton<SelectPairForTagUi>();
            collection.AddSingleton((s) => new EventAggregator(pluginInterface.ConfigDirectory.FullName,
                s.GetRequiredService<ILogger<EventAggregator>>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new DalamudContextMenu(pluginInterface));
            collection.AddSingleton((s) => new DalamudUtilService(s.GetRequiredService<ILogger<DalamudUtilService>>(),
                clientState, objectTable, framework, gameGui, condition, gameData, targetManager,
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddSingleton((s) => new DtrEntry(s.GetRequiredService<ILogger<DtrEntry>>(), dtrBar, s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<PairManager>(), s.GetRequiredService<ApiController>()));

            collection.AddSingleton<RedrawManager>();
            collection.AddSingleton((s) => new IpcCallerPenumbra(s.GetRequiredService<ILogger<IpcCallerPenumbra>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<RedrawManager>()));
            collection.AddSingleton((s) => new IpcCallerGlamourer(s.GetRequiredService<ILogger<IpcCallerGlamourer>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<RedrawManager>()));
            collection.AddSingleton((s) => new IpcCallerCustomize(s.GetRequiredService<ILogger<IpcCallerCustomize>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new IpcCallerHeels(s.GetRequiredService<ILogger<IpcCallerHeels>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new IpcCallerHonorific(s.GetRequiredService<ILogger<IpcCallerHonorific>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new IpcCallerMoodles(s.GetRequiredService<ILogger<IpcCallerMoodles>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<IpcCallerPenumbra>(), s.GetRequiredService<IpcCallerGlamourer>(),
                s.GetRequiredService<IpcCallerCustomize>(), s.GetRequiredService<IpcCallerHeels>(), s.GetRequiredService<IpcCallerHonorific>(),
                s.GetRequiredService<IpcCallerMoodles>()));

            collection.AddSingleton((s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TriangleCalculationConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ConfigurationMigrator(s.GetRequiredService<ILogger<ConfigurationMigrator>>(), pluginInterface));
            collection.AddSingleton<HubFactory>();

            // add scoped services
            collection.AddScoped<DrawEntityFactory>();
            collection.AddScoped<CacheMonitor>();
            collection.AddScoped<UiFactory>();
            collection.AddScoped<SelectTagForPairUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, GposeUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, JoinSyncshellUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CreateSyncshellUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();

            collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>((s) => new EditProfileUi(s.GetRequiredService<ILogger<EditProfileUi>>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<ApiController>(), pluginInterface.UiBuilder, s.GetRequiredService<UiSharedService>(),
                s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareProfileManager>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
            collection.AddScoped<IPopupHandler, ReportPopupHandler>();
            collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
            collection.AddScoped<IPopupHandler, CensusPopupHandler>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<TransientResourceManager>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<OnlinePlayerManager>();
            collection.AddScoped((s) => new UiService(s.GetRequiredService<ILogger<UiService>>(), pluginInterface.UiBuilder, s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<WindowSystem>(), s.GetServices<WindowMediatorSubscriberBase>(),
                s.GetRequiredService<UiFactory>(),
                s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddScoped((s) => new CommandManagerService(commandManager, s.GetRequiredService<PerformanceCollectorService>(),
                s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<MareConfigService>()));
            collection.AddScoped((s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(),
                s.GetRequiredService<MareMediator>(), notificationManager, chatGui, s.GetRequiredService<MareConfigService>()));
            collection.AddScoped((s) => new UiSharedService(s.GetRequiredService<ILogger<UiSharedService>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<DalamudUtilService>(),
                pluginInterface, s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<MareMediator>()));

            collection.AddHostedService(p => p.GetRequiredService<FileCacheManager>());
            collection.AddHostedService(p => p.GetRequiredService<MareMediator>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
            collection.AddHostedService(p => p.GetRequiredService<EventAggregator>());
            collection.AddHostedService(p => p.GetRequiredService<IpcProvider>());
            collection.AddHostedService(p => p.GetRequiredService<MarePlugin>());
        })
        .Build()
        .RunAsync(_pluginCts.Token);
    }

    public void Dispose()
    {
        _pluginCts.Cancel();
        _pluginCts.Dispose();
        _hostBuilderRunTask.Wait();
    }
}