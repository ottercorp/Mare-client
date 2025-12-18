using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
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
using NReco.Logging.File;
using System.Net.Http.Headers;
using System.Reflection;
using MareSynchronos.Services.CharaData;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;

namespace MareSynchronos;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        if (!Directory.Exists(pluginInterface.ConfigDirectory.FullName))
            Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
        var traceDir = Path.Join(pluginInterface.ConfigDirectory.FullName, "tracelog");
        if (!Directory.Exists(traceDir))
            Directory.CreateDirectory(traceDir);

        foreach (var file in Directory.EnumerateFiles(traceDir)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc).Skip(9))
        {
            int attempts = 0;
            bool deleted = false;
            while (!deleted && attempts < 5)
            {
                try
                {
                    file.Delete();
                    deleted = true;
                }
                catch
                {
                    attempts++;
                    Thread.Sleep(500);
                }
            }
        }

        _host = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging(pluginInterface.GetRequiredService<IPluginLog>(), pluginInterface.GetRequiredService<IDataManager>().HasModifiedGameDataFiles);
            lb.AddFile(Path.Combine(traceDir, $"mare-trace-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log"), (opt) =>
            {
                opt.Append = true;
                opt.RollingFilesConvention = FileLoggerOptions.FileRollingConvention.Ascending;
                opt.MinLevel = LogLevel.Trace;
                opt.FileSizeLimitBytes = 50 * 1024 * 1024;
            });
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
            collection.AddSingleton<ApiController>();
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
            collection.AddSingleton<XivDataAnalyzer>();
            collection.AddSingleton<CharacterAnalyzer>();
            collection.AddSingleton<TokenProvider>();
            collection.AddSingleton<PluginWarningNotificationService>();
            collection.AddSingleton<FileCompactor>();
            collection.AddSingleton<TagHandler>();
            collection.AddSingleton<IdDisplayHandler>();
            collection.AddSingleton<PlayerPerformanceService>();
            collection.AddSingleton<TransientResourceManager>();

            collection.AddSingleton<CharaDataManager>();
            collection.AddSingleton<CharaDataFileHandler>();
            collection.AddSingleton<CharaDataCharacterHandler>();
            collection.AddSingleton<CharaDataNearbyManager>();
            collection.AddSingleton<CharaDataGposeTogetherManager>();

            collection.AddSingleton(s => new VfxSpawnManager(s.GetRequiredService<ILogger<VfxSpawnManager>>(),
                pluginInterface.GetRequiredService<IGameInteropProvider>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new BlockedCharacterHandler(s.GetRequiredService<ILogger<BlockedCharacterHandler>>(), pluginInterface.GetRequiredService<IGameInteropProvider>()));
            collection.AddSingleton((s) => new IpcProvider(s.GetRequiredService<ILogger<IpcProvider>>(),
                pluginInterface,
                 s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<PairManager>(),
                s.GetRequiredService<CharaDataManager>(), s.GetRequiredService<MareMediator>(),
                s.GetRequiredService<ApiController>(), s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<IpcCallerChatTwo>()));
            collection.AddSingleton<SelectPairForTagUi>();
            collection.AddSingleton((s) => new EventAggregator(pluginInterface.ConfigDirectory.FullName,
                s.GetRequiredService<ILogger<EventAggregator>>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new DalamudUtilService(s.GetRequiredService<ILogger<DalamudUtilService>>(),
                pluginInterface, s.GetRequiredService<BlockedCharacterHandler>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddSingleton((s) => new DtrEntry(s.GetRequiredService<ILogger<DtrEntry>>(), pluginInterface.GetRequiredService<IDtrBar>(), s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<PairManager>(), s.GetRequiredService<ApiController>()));
            collection.AddSingleton(s => new PairManager(s.GetRequiredService<ILogger<PairManager>>(), s.GetRequiredService<PairFactory>(),
                s.GetRequiredService<MareConfigService>(), s.GetRequiredService<MareMediator>(), pluginInterface.GetRequiredService<IContextMenu>()));
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
            collection.AddSingleton((s) => new IpcCallerPetNames(s.GetRequiredService<ILogger<IpcCallerPetNames>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton((s) => new IpcCallerBrio(s.GetRequiredService<ILogger<IpcCallerBrio>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>()));
            collection.AddSingleton((s) => new IpcCallerChatTwo(s.GetRequiredService<ILogger<IpcCallerChatTwo>>(), pluginInterface));
            collection.AddSingleton((s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<IpcCallerPenumbra>(), s.GetRequiredService<IpcCallerGlamourer>(),
                s.GetRequiredService<IpcCallerCustomize>(), s.GetRequiredService<IpcCallerHeels>(), s.GetRequiredService<IpcCallerHonorific>(),
                s.GetRequiredService<IpcCallerMoodles>(), s.GetRequiredService<IpcCallerPetNames>(), s.GetRequiredService<IpcCallerBrio>(),
                s.GetRequiredService<IpcCallerChatTwo>()));
            collection.AddSingleton((s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<DalamudUtilService>(),
                pluginInterface.GetRequiredService<INotificationManager>(), pluginInterface.GetRequiredService<IChatGui>(), s.GetRequiredService<MareConfigService>()));
            collection.AddSingleton((s) =>
            {
                var config = s.GetRequiredService<MareConfigService>().Current;
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 15,
                    EnableMultipleHttp2Connections = true,
                    SslOptions = {
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]
                    },
                    UseProxy = config.UseManualProxy,
                    Proxy = config.UseManualProxy
                        ? new WebProxy($"{config.ProxyProtocol}://{config.ProxyHost}:{config.ProxyPort}", true)
                        : null,
                };
                var httpClient = new HttpClient(handler);
                httpClient.DefaultRequestVersion = HttpVersion.Version20;
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build + "." + ver!.Revision));
                return httpClient;
            });
            collection.AddSingleton((s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new XivDataStorageService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new PlayerPerformanceConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new CharaDataConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<MareConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<ServerConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<NotesConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<ServerTagConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<TransientConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<XivDataStorageService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<PlayerPerformanceConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<CharaDataConfigService>());
            collection.AddSingleton<ConfigurationMigrator>();
            collection.AddSingleton<ConfigurationSaveService>();

            collection.AddSingleton<HubFactory>();

            // add scoped services
            collection.AddScoped<DrawEntityFactory>();
            collection.AddScoped<CacheMonitor>();
            collection.AddScoped<UiFactory>();
            collection.AddScoped<SelectTagForPairUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, JoinSyncshellUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CreateSyncshellUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CharaDataHubUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, ChatUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PFinderWindow>((s) => new PFinderWindow(s.GetRequiredService<ILogger<PFinderWindow>>(),
                s.GetRequiredService<MareConfigService>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>(),
                s.GetRequiredService<ApiController>(), pluginInterface.GetRequiredService<IChatGui>(), s.GetRequiredService<UiSharedService>()));
            collection.AddScoped<WindowMediatorSubscriberBase, ChangelogUi>();

            collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>((s) => new EditProfileUi(s.GetRequiredService<ILogger<EditProfileUi>>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<UiSharedService>(), s.GetRequiredService<FileDialogManager>(),
                s.GetRequiredService<MareProfileManager>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
            collection.AddScoped<IPopupHandler, ReportPopupHandler>();
            collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
            collection.AddScoped<IPopupHandler, CensusPopupHandler>();
            collection.AddScoped<IPopupHandler, PFinderPopupHandler>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<VisibleUserDataDistributor>();
            collection.AddScoped((s) => new UiService(s.GetRequiredService<ILogger<UiService>>(), pluginInterface.UiBuilder, s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<WindowSystem>(), s.GetServices<WindowMediatorSubscriberBase>(),
                s.GetRequiredService<UiFactory>(),
                s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddScoped((s) => new CommandManagerService(pluginInterface.GetRequiredService<ICommandManager>(), s.GetRequiredService<PerformanceCollectorService>(),
                s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<PairManager>()));
            collection.AddScoped((s) => new UiSharedService(s.GetRequiredService<ILogger<UiSharedService>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<DalamudUtilService>(),
                pluginInterface, pluginInterface.GetRequiredService<ITextureProvider>(), s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<TokenProvider>(),
                s.GetRequiredService<MareMediator>()));

            collection.AddHostedService(p => p.GetRequiredService<ConfigurationSaveService>());
            collection.AddHostedService(p => p.GetRequiredService<MareMediator>());
            collection.AddHostedService(p => p.GetRequiredService<NotificationService>());
            collection.AddHostedService(p => p.GetRequiredService<FileCacheManager>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
            collection.AddHostedService(p => p.GetRequiredService<EventAggregator>());
            collection.AddHostedService(p => p.GetRequiredService<IpcProvider>());
            collection.AddHostedService(p => p.GetRequiredService<MarePlugin>());
        })
        .Build();

        _ = _host.StartAsync();
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}