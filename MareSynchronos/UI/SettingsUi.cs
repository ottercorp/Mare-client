using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MareSynchronos.UI;

public class SettingsUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly CacheMonitor _cacheMonitor;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly HttpClient _httpClient;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileUploadManager _fileTransferManager;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly IpcManager _ipcManager;
    private readonly PairManager _pairManager;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private readonly IProgress<(int, int, FileCacheEntity)> _validationProgress;
    private (int, int, FileCacheEntity) _currentProgress;
    private bool _deleteAccountPopupModalShown = false;
    private bool _deleteFilesPopupModalShown = false;
    private string _lastTab = string.Empty;
    private bool? _notesSuccessfullyApplied = null;
    private bool _overwriteExistingLabels = false;
    private bool _readClearCache = false;
    private int _selectedEntry = -1;
    private string _uidToAddForIgnore = string.Empty;
    private CancellationTokenSource? _validationCts;

    private bool useManualProxy;
    private string proxyProtocol = string.Empty;
    private string proxyHost = string.Empty;
    private int proxyPort;
    private int proxyProtocolIndex;
    private string proxyStatus = "未知";
    private readonly string[] proxyProtocols = new string[] { "http", "https", "socks5" };
    private readonly string[] colors = new[] { "1", "17", "25", "37", "43", "48", "524" };
    private readonly unsafe RaptureAtkModule* raptureAtkModule = RaptureAtkModule.Instance();

    private Task<List<FileCacheEntity>>? _validationTask;
    private bool _wasOpen = false;

    public SettingsUi(ILogger<SettingsUi> logger,
        UiSharedService uiShared, MareConfigService configService,
        PairManager pairManager,
        ServerConfigurationManager serverConfigurationManager,
        PlayerPerformanceConfigService playerPerformanceConfigService,
        MareMediator mediator, PerformanceCollectorService performanceCollector,
        FileUploadManager fileTransferManager,
        FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager,
        FileCompactor fileCompactor, ApiController apiController,
        IpcManager ipcManager, CacheMonitor cacheMonitor,
        DalamudUtilService dalamudUtilService, HttpClient httpClient) : base(logger, mediator, "Mare设置", performanceCollector)
    {
        _configService = configService;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _performanceCollector = performanceCollector;
        _fileTransferManager = fileTransferManager;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _apiController = apiController;
        _ipcManager = ipcManager;
        _cacheMonitor = cacheMonitor;
        _dalamudUtilService = dalamudUtilService;
        _httpClient = httpClient;
        _fileCompactor = fileCompactor;
        _uiShared = uiShared;
        AllowClickthrough = false;
        AllowPinning = false;
        _validationProgress = new Progress<(int, int, FileCacheEntity)>(v => _currentProgress = v);

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(800, 2000),
        };

        Mediator.Subscribe<OpenSettingsUiMessage>(this, (_) => Toggle());
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) => LastCreatedCharacterData = msg.CharacterData);
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
    }

    public CharacterData? LastCreatedCharacterData { private get; set; }
    private ApiController ApiController => _uiShared.ApiController;

    public override void OnOpen()
    {
        _uiShared.ResetOAuthTasksState();
        _speedTestCts = new();
    }

    public override void OnClose()
    {
        _uiShared.EditTrackerPosition = false;
        _uidToAddForIgnore = string.Empty;
        _secretKeysConversionCts = _secretKeysConversionCts.CancelRecreate();
        _downloadServersTask = null;
        _speedTestTask = null;
        _speedTestCts?.Cancel();
        _speedTestCts?.Dispose();
        _speedTestCts = null;

        base.OnClose();
    }

    protected override void DrawInternal()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawSettingsContent();
    }
    private static bool InputDtrColors(string label, ref DtrEntry.Colors colors)
    {
        using var id = ImRaii.PushId(label);
        var innerSpacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var foregroundColor = ConvertColor(colors.Foreground);
        var glowColor = ConvertColor(colors.Glow);

        var ret = ImGui.ColorEdit3("###foreground", ref foregroundColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.Uint8);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("字体颜色 - 设置为纯黑 (#000000) 使用默认颜色");

        ImGui.SameLine(0.0f, innerSpacing);
        ret |= ImGui.ColorEdit3("###glow", ref glowColor, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.Uint8);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("发光颜色 - 设置为纯黑 (#000000) 使用默认颜色");

        ImGui.SameLine(0.0f, innerSpacing);
        ImGui.TextUnformatted(label);

        if (ret)
            colors = new(ConvertBackColor(foregroundColor), ConvertBackColor(glowColor));

        return ret;

        static Vector3 ConvertColor(uint color)
            => unchecked(new((byte)color / 255.0f, (byte)(color >> 8) / 255.0f, (byte)(color >> 16) / 255.0f));

        static uint ConvertBackColor(Vector3 color)
            => byte.CreateSaturating(color.X * 255.0f) | ((uint)byte.CreateSaturating(color.Y * 255.0f) << 8) | ((uint)byte.CreateSaturating(color.Z * 255.0f) << 16);
    }

    private void DrawBlockedTransfers()
    {
        _lastTab = "BlockedTransfers";
        UiSharedService.ColorTextWrapped("您试图上传或下载但创建者禁止传输的文件将显示在此处。 " +
                             "如果您在此处看到驱动器中的文件路径，则不允许上载这些文件。如果你看到哈希值，那么这些文件是不允许下载的。 " +
                             "让与您配对的朋友通过其他方式向你发送有问题的mod、自己获取mod或去纠缠mod创建者允许其通过月海发送。",
            ImGuiColors.DalamudGrey);

        if (ImGui.BeginTable("TransfersTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(
                $"哈希/文件名");
            ImGui.TableSetupColumn($"被禁止");

            ImGui.TableHeadersRow();

            foreach (var item in _fileTransferOrchestrator.ForbiddenTransfers)
            {
                ImGui.TableNextColumn();
                if (item is UploadFileTransfer transfer)
                {
                    ImGui.TextUnformatted(transfer.LocalFile);
                }
                else
                {
                    ImGui.TextUnformatted(item.Hash);
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.ForbiddenBy);
            }
            ImGui.EndTable();
        }
    }

    public void LoadProxyConfig()
    {

        this.useManualProxy = _configService.Current.UseManualProxy;
        this.proxyProtocol = _configService.Current.ProxyProtocol;
        this.proxyHost = _configService.Current.ProxyHost;
        this.proxyPort = _configService.Current.ProxyPort;
        this.proxyProtocolIndex = Array.IndexOf(this.proxyProtocols, this.proxyProtocol);
        if (this.proxyProtocolIndex == -1)
            this.proxyProtocolIndex = 0;
    }
    private void DrawCurrentTransfers()
    {
        _lastTab = "Transfers";
        _uiShared.BigText("代理设置");
        LoadProxyConfig();
        ImGui.TextColoredWrapped(ImGuiColors.DalamudRed, "设置 Mare 所使用的网络代理,会影响到文件同步的连接,保存后重启插件生效");
        if (ImGui.Checkbox("手动配置代理", ref this.useManualProxy))
        {
            _configService.Current.UseManualProxy = this.useManualProxy;
            _configService.Save();
        }
        if (this.useManualProxy)
        {
            ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey, "在更改下方选项时，请确保你知道你在做什么，否则不要随便更改。");
            ImGui.Text("协议");
            ImGui.SameLine();
            if (ImGui.Combo("##proxyProtocol", ref this.proxyProtocolIndex, this.proxyProtocols, this.proxyProtocols.Length))
            {
                this.proxyProtocol = this.proxyProtocols[this.proxyProtocolIndex];
                _configService.Current.ProxyProtocol = this.proxyProtocol;
                _configService.Save();
            }
            ImGui.Text("地址");
            ImGui.SameLine();
            if (ImGui.InputText("##proxyHost", ref this.proxyHost, 100))
            {
                _configService.Current.ProxyHost = this.proxyHost;
                _configService.Save();
            }
            ImGui.Text("端口");
            ImGui.SameLine();
            if (ImGui.InputInt("##proxyPort", ref this.proxyPort))
            {
                _configService.Current.ProxyPort = this.proxyPort;
                _configService.Save();
            }
        }

        if (ImGui.Button("测试GitHub连接"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    this.proxyStatus = "测试中";
                    var handler = new HttpClientHandler();
                    if (this.useManualProxy)
                    {
                        handler.UseProxy = true;
                        handler.Proxy = new WebProxy($"{this.proxyProtocol}://{this.proxyHost}:{this.proxyPort}", true);
                    }
                    else
                    {
                        handler.UseProxy = true;
                        handler.Proxy = new WebProxy();
                    }
                    var httpClient = new HttpClient(handler);
                    httpClient.Timeout = TimeSpan.FromSeconds(3);
                    _ = await httpClient.GetStringAsync("https://raw.githubusercontent.com/ottercorp/dalamud-distrib/main/version");
                    this.proxyStatus = "有效";
                }
                catch (Exception)
                {
                    this.proxyStatus = "无效";
                }
            });
        }

        var proxyStatusColor = ImGuiColors.DalamudWhite;
        switch (this.proxyStatus)
        {
            case "测试中":
                proxyStatusColor = ImGuiColors.DalamudYellow;
                break;
            case "有效":
                proxyStatusColor = ImGuiColors.ParsedGreen;
                break;
            case "无效":
                proxyStatusColor = ImGuiColors.DalamudRed;
                break;
            default: break;
        }

        ImGui.TextColored(proxyStatusColor, $"代理测试结果: {this.proxyStatus}");

        ImGui.Separator();
        _uiShared.BigText("传输设置");

        int maxParallelDownloads = _configService.Current.ParallelDownloads;
        bool useAlternativeUpload = _configService.Current.UseAlternativeFileUpload;
        int downloadSpeedLimit = _configService.Current.DownloadSpeedLimitInBytes;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("全局下载限速");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("###speedlimit", ref downloadSpeedLimit))
        {
            _configService.Current.DownloadSpeedLimitInBytes = downloadSpeedLimit;
            _configService.Save();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        _uiShared.DrawCombo("###speed", [DownloadSpeeds.Bps, DownloadSpeeds.KBps, DownloadSpeeds.MBps],
            (s) => s switch
            {
                DownloadSpeeds.Bps => "Byte/s",
                DownloadSpeeds.KBps => "KB/s",
                DownloadSpeeds.MBps => "MB/s",
                _ => throw new NotSupportedException()
            }, (s) =>
            {
                _configService.Current.DownloadSpeedType = s;
                _configService.Save();
                Mediator.Publish(new DownloadLimitChangedMessage());
            }, _configService.Current.DownloadSpeedType);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("0 = 无限制");

        if (ImGui.SliderInt("最大并行下载数(重启插件生效)", ref maxParallelDownloads, 1, 20))
        {
            _configService.Current.ParallelDownloads = maxParallelDownloads;
            _configService.Save();
        }

        if (ImGui.Checkbox("使用其他上传方法", ref useAlternativeUpload))
        {
            _configService.Current.UseAlternativeFileUpload = useAlternativeUpload;
            _configService.Save();
        }
        _uiShared.DrawHelpText("尝试一次性上传文件，而不是流式上传。通常不需要启用。如果您有上传问题，那么请使用这个功能。");

        ImGui.Separator();
        _uiShared.BigText("传输UI");

        bool showTransferWindow = _configService.Current.ShowTransferWindow;
        if (ImGui.Checkbox("显示单独的传输窗口", ref showTransferWindow))
        {
            _configService.Current.ShowTransferWindow = showTransferWindow;
            _configService.Save();
        }
        _uiShared.DrawHelpText($"下载窗口将显示未完成下载的当前进度。{Environment.NewLine}{Environment.NewLine}" +
            $"W/Q/P/D代表什么？{Environment.NewLine}W = 等待下载（请参阅最大并行下载量）{Environment.NewLine}" +
            $"Q = 在服务器上排队，等待队列就绪信号{Environment.NewLine}" +
            $"P = 正在处理下载（即下载中）{Environment.NewLine}" +
            $"D = 解压缩下载");
        if (!_configService.Current.ShowTransferWindow) ImGui.BeginDisabled();
        ImGui.Indent();
        bool editTransferWindowPosition = _uiShared.EditTrackerPosition;
        if (ImGui.Checkbox("编辑传输窗口位置", ref editTransferWindowPosition))
        {
            _uiShared.EditTrackerPosition = editTransferWindowPosition;
        }
        ImGui.Unindent();
        if (!_configService.Current.ShowTransferWindow) ImGui.EndDisabled();

        bool showTransferBars = _configService.Current.ShowTransferBars;
        if (ImGui.Checkbox("在玩家下方显示的传输条", ref showTransferBars))
        {
            _configService.Current.ShowTransferBars = showTransferBars;
            _configService.Save();
        }
        _uiShared.DrawHelpText("这将在下载过程中在您下载的玩家脚下呈现进度条。");

        if (!showTransferBars) ImGui.BeginDisabled();
        ImGui.Indent();
        bool transferBarShowText = _configService.Current.TransferBarsShowText;
        if (ImGui.Checkbox("显示下载文本", ref transferBarShowText))
        {
            _configService.Current.TransferBarsShowText = transferBarShowText;
            _configService.Save();
        }
        _uiShared.DrawHelpText("在传输条中显示下载文本（下载的MiB大小）");
        int transferBarWidth = _configService.Current.TransferBarsWidth;
        if (ImGui.SliderInt("传输条宽度", ref transferBarWidth, 10, 500))
        {
            _configService.Current.TransferBarsWidth = transferBarWidth;
            _configService.Save();
        }
        _uiShared.DrawHelpText("显示的传输条的宽度（永远不会小于显示的文本的宽度）");
        int transferBarHeight = _configService.Current.TransferBarsHeight;
        if (ImGui.SliderInt("传输条高度", ref transferBarHeight, 2, 50))
        {
            _configService.Current.TransferBarsHeight = transferBarHeight;
            _configService.Save();
        }
        _uiShared.DrawHelpText("显示的传输条的高度（永远不会低于显示的文本）");
        bool showUploading = _configService.Current.ShowUploading;
        if (ImGui.Checkbox("在当前正在上传的玩家下方显示“上传”文本", ref showUploading))
        {
            _configService.Current.ShowUploading = showUploading;
            _configService.Save();
        }
        _uiShared.DrawHelpText("这将在正在上传数据的玩家脚下呈现一个“上传”文本。");

        ImGui.Unindent();
        if (!showUploading) ImGui.BeginDisabled();
        ImGui.Indent();
        bool showUploadingBigText = _configService.Current.ShowUploadingBigText;
        if (ImGui.Checkbox("大字体“上传”文本", ref showUploadingBigText))
        {
            _configService.Current.ShowUploadingBigText = showUploadingBigText;
            _configService.Save();
        }
        _uiShared.DrawHelpText("这将以更大的字体呈现“上传”文本。");

        ImGui.Unindent();

        if (!showUploading) ImGui.EndDisabled();
        if (!showTransferBars) ImGui.EndDisabled();

        if (_apiController.IsConnected)
        {
            ImGuiHelpers.ScaledDummy(5);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(10);
            using var tree = ImRaii.TreeNode("服务器测速");
            if (tree)
            {
                if (_downloadServersTask == null || ((_downloadServersTask?.IsCompleted ?? false) && (!_downloadServersTask?.IsCompletedSuccessfully ?? false)))
                {
                    if (_uiShared.IconTextButton(FontAwesomeIcon.GroupArrowsRotate, "更新下载服务器列表"))
                    {
                        _downloadServersTask = GetDownloadServerList();
                    }
                }
                if (_downloadServersTask != null && _downloadServersTask.IsCompleted && !_downloadServersTask.IsCompletedSuccessfully)
                {
                    UiSharedService.ColorTextWrapped("更新下载服务器列表失败, 查看 /xllog 获取更多信息", ImGuiColors.DalamudRed);
                }
                if (_downloadServersTask != null && _downloadServersTask.IsCompleted && _downloadServersTask.IsCompletedSuccessfully)
                {
                    if (_speedTestTask == null || _speedTestTask.IsCompleted)
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowRight, "开始测速"))
                        {
                            _speedTestTask = RunSpeedTest(_downloadServersTask.Result!, _speedTestCts?.Token ?? CancellationToken.None);
                        }
                    }
                    else if (!_speedTestTask.IsCompleted)
                    {
                        UiSharedService.ColorTextWrapped("正在测速...", ImGuiColors.DalamudYellow);
                        UiSharedService.ColorTextWrapped("请稍后, 基于服务器状态和连接速度这可能需要一段时间...", ImGuiColors.DalamudYellow);
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Ban, "取消测速"))
                        {
                            _speedTestCts?.Cancel();
                            _speedTestCts?.Dispose();
                            _speedTestCts = new();
                        }
                    }
                    if (_speedTestTask != null && _speedTestTask.IsCompleted)
                    {
                        if (_speedTestTask.Result != null && _speedTestTask.Result.Count != 0)
                        {
                            foreach (var result in _speedTestTask.Result)
                            {
                                UiSharedService.TextWrapped(result);
                            }
                        }
                        else
                        {
                            UiSharedService.ColorTextWrapped("测速完成, 无结果", ImGuiColors.DalamudYellow);
                        }
                    }
                }
            }
            ImGuiHelpers.ScaledDummy(10);
        }

        ImGui.Separator();
        _uiShared.BigText("当前传输");

        if (ImGui.BeginTabBar("TransfersTabBar"))
        {
            if (ApiController.ServerState is ServerState.Connected && ImGui.BeginTabItem("传输"))
            {
                ImGui.TextUnformatted("上传");
                if (ImGui.BeginTable("UploadsTable", 3))
                {
                    ImGui.TableSetupColumn("文件");
                    ImGui.TableSetupColumn("已上传");
                    ImGui.TableSetupColumn("大小");
                    ImGui.TableHeadersRow();
                    foreach (var transfer in _fileTransferManager.CurrentUploads.ToArray())
                    {
                        var color = UiSharedService.UploadColor((transfer.Transferred, transfer.Total));
                        var col = ImRaii.PushColor(ImGuiCol.Text, color);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(transfer.Hash);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(UiSharedService.ByteToString(transfer.Transferred));
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(UiSharedService.ByteToString(transfer.Total));
                        col.Dispose();
                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }
                ImGui.Separator();
                ImGui.TextUnformatted("下载");
                if (ImGui.BeginTable("DownloadsTable", 4))
                {
                    ImGui.TableSetupColumn("用户");
                    ImGui.TableSetupColumn("服务器");
                    ImGui.TableSetupColumn("文件");
                    ImGui.TableSetupColumn("下载");
                    ImGui.TableHeadersRow();

                    foreach (var transfer in _currentDownloads.ToArray())
                    {
                        var userName = transfer.Key.Name;
                        foreach (var entry in transfer.Value)
                        {
                            var color = UiSharedService.UploadColor((entry.Value.TransferredBytes, entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(userName);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry.Key);
                            var col = ImRaii.PushColor(ImGuiCol.Text, color);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(entry.Value.TransferredFiles + "/" + entry.Value.TotalFiles);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(UiSharedService.ByteToString(entry.Value.TransferredBytes) + "/" + UiSharedService.ByteToString(entry.Value.TotalBytes));
                            ImGui.TableNextColumn();
                            col.Dispose();
                            ImGui.TableNextRow();
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("被阻止的传输"))
            {
                DrawBlockedTransfers();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private Task<List<string>?>? _downloadServersTask = null;
    private Task<List<string>?>? _speedTestTask = null;
    private CancellationTokenSource? _speedTestCts;

    private async Task<List<string>?> RunSpeedTest(List<string> servers, CancellationToken token)
    {
        List<string> speedTestResults = new();
        foreach (var server in servers)
        {
            HttpResponseMessage? result = null;
            Stopwatch? st = null;
            try
            {
                result = await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Get, new Uri(new Uri(server.Replace("ffxiv.wang:6200", "zettaigame.com").Replace("files/","")), "speedtest/run"), token, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                result.EnsureSuccessStatusCode();
                using CancellationTokenSource speedtestTimeCts = new();
                speedtestTimeCts.CancelAfter(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(speedtestTimeCts.Token, token);
                long readBytes = 0;
                st = Stopwatch.StartNew();
                try
                {
                    var stream = await result.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
                    byte[] buffer = new byte[8192];
                    while (!speedtestTimeCts.Token.IsCancellationRequested)
                    {
                        var currentBytes = await stream.ReadAsync(buffer, linkedCts.Token).ConfigureAwait(false);
                        if (currentBytes == 0)
                            break;
                        readBytes += currentBytes;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("对 {server} 的测速已取消", server);
                }
                st.Stop();
                _logger.LogInformation("下载了 {bytes} 自 {server} 在 {time}", UiSharedService.ByteToString(readBytes), server, st.Elapsed);
                var bps = (long)((readBytes) / st.Elapsed.TotalSeconds);
                speedTestResults.Add($"{server}: 下载速度 ~{UiSharedService.ByteToString(bps)}/s");
            }
            catch (HttpRequestException ex)
            {
                if (result != null)
                {
                    var res = await result!.Content.ReadAsStringAsync().ConfigureAwait(false);
                    speedTestResults.Add($"{server}: {ex.Message} - {res}");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("对 {server} 的测速已取消", server);
                speedTestResults.Add($"{server}: 用户手动取消");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "出现错误");
            }
            finally
            {
                st?.Stop();
            }
        }
        return speedTestResults;
    }

    private async Task<List<string>?> GetDownloadServerList()
    {
        try
        {
            var result = await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Get, new Uri(_fileTransferOrchestrator.FilesCdnUri!, "downloadServers"), CancellationToken.None).ConfigureAwait(false);
            result.EnsureSuccessStatusCode();
            return await JsonSerializer.DeserializeAsync<List<string>>(await result.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取下载服务器列表失败");
            throw;
        }
    }

    private void DrawDebug()
    {
        _lastTab = "Debug";

        _uiShared.BigText("调试");
#if DEBUG
        if (LastCreatedCharacterData != null && ImGui.TreeNode("Last created character data"))
        {
            foreach (var l in JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }).Split('\n'))
            {
                ImGui.TextUnformatted($"{l}");
            }

            ImGui.TreePop();
        }
#endif
        if (_uiShared.IconTextButton(FontAwesomeIcon.Copy, "[DEBUG] 将上次创建的角色数据复制到剪贴板"))
        {
            if (LastCreatedCharacterData != null)
            {
                ImGui.SetClipboardText(JsonSerializer.Serialize(LastCreatedCharacterData, new JsonSerializerOptions() { WriteIndented = true }));
            }
            else
            {
                ImGui.SetClipboardText("ERROR: 没有创建角色数据，无法复制。");
            }
        }
        UiSharedService.AttachToolTip("在报告被服务器拒绝的mod时使用此选项。");

        _uiShared.DrawCombo("日志等级", Enum.GetValues<LogLevel>(), (l) => l.ToString(), (l) =>
        {
            _configService.Current.LogLevel = l;
            _configService.Save();
        }, _configService.Current.LogLevel);

        bool logPerformance = _configService.Current.LogPerformance;
        if (ImGui.Checkbox("日志性能计数器", ref logPerformance))
        {
            _configService.Current.LogPerformance = logPerformance;
            _configService.Save();
        }
        _uiShared.DrawHelpText("启用此功能可能会对性能产生（轻微）影响。不建议长时间启用此功能。");

        using (ImRaii.Disabled(!logPerformance))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "输出性能状态到 /xllog"))
            {
                _performanceCollector.PrintPerformanceStats();
            }
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "输出性能状态(最近60秒)到 /xllog"))
            {
                _performanceCollector.PrintPerformanceStats(60);
            }
        }

        bool stopWhining = _configService.Current.DebugStopWhining;
        if (ImGui.Checkbox("不要提示我游戏文件被修改或细节层次未关闭", ref stopWhining))
        {
            _configService.Current.DebugStopWhining = stopWhining;
            _configService.Save();
        }
        _uiShared.DrawHelpText("无论是否打开本选项都会将你的Log标记为UNSUPPORTED, 你将不会接受到管理们的帮助." + UiSharedService.TooltipSeparator
            + "打开细节层次可能导致游戏崩溃.");
#if DEBUG
        if (ImGui.TreeNode("Test###DEBUG"))
        {
            TestMethod();
            ImGui.TreePop();
        }
#endif
    }

    private void TestMethod()
    {
        foreach (Pair onlineUserPair in _pairManager.GetOnlineUserPairs())
        {
            if (onlineUserPair.UserPair.OtherPermissions.IsEnabledShareLocation())
            {
                var address = _apiController.GetUserLocation(onlineUserPair.UserData.UID);
                if (string.IsNullOrEmpty(address))
                {
                    ImGui.TextUnformatted($"{onlineUserPair.UserData.AliasOrUID} : 未知地点");
                }
                else ImGui.TextUnformatted($"{onlineUserPair.UserData.AliasOrUID} : {address}");
            }
        }
        if (ImGui.Button("打印位置###"))
        {
            Log.Warning($"{_dalamudUtilService.LocationToString(_dalamudUtilService.GetMapDataAsync().Result)}");
            Log.Warning($"{_dalamudUtilService.GetMapDataAsync().Result}");
        }

    }

    private void DrawFileStorageSettings()
    {
        _lastTab = "FileCache";

        _uiShared.BigText("导出MCDF");

        ImGuiHelpers.ScaledDummy(10);

        UiSharedService.ColorTextWrapped("导出 MCDF 功能已被移动.", ImGuiColors.DalamudYellow);
        ImGuiHelpers.ScaledDummy(5);
        UiSharedService.TextWrapped("它被移动到了主界面下的 \"你的角色\" 菜单中 (");
        ImGui.SameLine();
        _uiShared.IconText(FontAwesomeIcon.UserCog);
        ImGui.SameLine();
        UiSharedService.TextWrapped(") -> \"角色数据中心\".");
        if (_uiShared.IconTextButton(FontAwesomeIcon.Running, "打开Mare角色数据中心"))
        {
            Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        UiSharedService.TextWrapped("注意: 本入口将在未来被移除. 请从主界面打开角色数据中心.");
        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();

        _uiShared.BigText("存储");

        UiSharedService.TextWrapped("月海将永久存储配对用户所下载的文件。这是为了提高加载性能并减少下载量。" +
            "是否清除文件将通过设置的最大存储大小数值进行自我管理。请酌情地设置存储大小。无需手动清除存储文件。");

        _uiShared.DrawFileScanState();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("当前使用的Penumbra文件夹: " + (_cacheMonitor.PenumbraWatcher?.Path ?? "文件夹不存在"));
        if (string.IsNullOrEmpty(_cacheMonitor.PenumbraWatcher?.Path))
        {
            ImGui.SameLine();
            using var id = ImRaii.PushId("penumbraMonitor");
            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "重新扫描文件夹"))
            {
                _cacheMonitor.StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            }
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("当前使用的Mare缓存文件夹: " + (_cacheMonitor.MareWatcher?.Path ?? "文件夹不存在"));
        if (string.IsNullOrEmpty(_cacheMonitor.MareWatcher?.Path))
        {
            ImGui.SameLine();
            using var id = ImRaii.PushId("mareMonitor");
            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "尝试刷新文件夹状态"))
            {
                _cacheMonitor.StartMareWatcher(_configService.Current.CacheFolder);
            }
        }
        if (_cacheMonitor.MareWatcher == null || _cacheMonitor.PenumbraWatcher == null)
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Play, "恢复文件夹读写"))
            {
                _cacheMonitor.StartMareWatcher(_configService.Current.CacheFolder);
                _cacheMonitor.StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
                _cacheMonitor.InvokeScan();
            }
            UiSharedService.AttachToolTip("尝试恢复对Pen和Mare缓存文件夹的读取与写入. "
                + "这会首先触发一次完整性扫描." + Environment.NewLine
                + "如果点击该按钮后没有反应,请输入 /xllog 查看报错");
        }
        else
        {
            using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
            {
                if (_uiShared.IconTextButton(FontAwesomeIcon.Stop, "停止文件夹读写"))
                {
                    _cacheMonitor.StopMonitoring();
                }
            }
            UiSharedService.AttachToolTip("停止对Pen和Mare缓存文件夹的读取与写入. "
                + "除非你需要修改以上两个文件夹的位置或手工修改Mare缓存的文件,否则请勿随意停止文件夹读写." + Environment.NewLine
                + "完成修改后,你需要手动重新启动对这些文件夹的读写."
                + UiSharedService.TooltipSeparator + "按住CTRL然后才能点击该按钮");
        }

        _uiShared.DrawCacheDirectorySetting();
        ImGui.AlignTextToFramePadding();
        if (_cacheMonitor.FileCacheSize >= 0)
            ImGui.TextUnformatted($"当前使用的本地存储： {UiSharedService.ByteToString(_cacheMonitor.FileCacheSize)}");
        else
            ImGui.TextUnformatted($"当前使用的本地存储： 计算中...");
        ImGui.TextUnformatted($"剩余可用空间: {UiSharedService.ByteToString(_cacheMonitor.FileCacheDriveFree)}");
        bool useFileCompactor = _configService.Current.UseCompactor;
        bool isLinux = _dalamudUtilService.IsWine;
        if (!useFileCompactor && !isLinux)
        {
            UiSharedService.ColorTextWrapped("提示: 使用文件系统压缩可以减少Mare缓存的占用空间", ImGuiColors.DalamudYellow);
        }
        if (isLinux || !_cacheMonitor.StorageisNTFS) ImGui.BeginDisabled();
        if (ImGui.Checkbox("使用文件系统压缩", ref useFileCompactor))
        {
            _configService.Current.UseCompactor = useFileCompactor;
            _configService.Save();
        }
        _uiShared.DrawHelpText("文件系统压缩可以大幅减少文件在硬盘上占用的空间。在性能较低的CPU上可能会产生轻微的负荷。" + Environment.NewLine
            + "建议保持启用状态以节省空间。");
        ImGui.SameLine();
        if (!_fileCompactor.MassCompactRunning)
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.FileArchive, "压缩存储中的所有文件"))
            {
                _ = Task.Run(() =>
                {
                    _fileCompactor.CompactStorage(compress: true);
                    _cacheMonitor.RecalculateFileCacheSize(CancellationToken.None);
                });
            }
            UiSharedService.AttachToolTip("这将对您当前本地存储的所有月海同步文件进行压缩。" + Environment.NewLine
                + "如果您保持启用文件系统压缩，则不需要手动运行此操作。");
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.File, "解压缩存储中的所有文件"))
            {
                _ = Task.Run(() =>
                {
                    _fileCompactor.CompactStorage(compress: false);
                    _cacheMonitor.RecalculateFileCacheSize(CancellationToken.None);
                });
            }
            UiSharedService.AttachToolTip("这将对当前本地存储的所有月海同步文件进行解压缩。");
        }
        else
        {
            UiSharedService.ColorText($"文件压缩程序当前正在运行 ({_fileCompactor.Progress})", ImGuiColors.DalamudYellow);
        }
        if (isLinux || !_cacheMonitor.StorageisNTFS)
        {
            ImGui.EndDisabled();
            ImGui.TextUnformatted("文件系统压缩仅在NTFS硬盘上可用.");
        }
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        ImGui.Separator();
        UiSharedService.TextWrapped("文件完整性检查可以检查本地储存的Mare缓存文件是否存在错误. " +
            "删除本地Mare缓存前请务必先进行文件完整性检查. " + Environment.NewLine +
            "完整性检查中会有较高CPU和硬盘占用,所需时间与本地缓存的文件数量有关.");
        using (ImRaii.Disabled(_validationTask != null && !_validationTask.IsCompleted))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Check, "开始文件完整性检查"))
            {
                _validationCts?.Cancel();
                _validationCts?.Dispose();
                _validationCts = new();
                var token = _validationCts.Token;
                _validationTask = Task.Run(() => _fileCacheManager.ValidateLocalIntegrity(_validationProgress, token));
            }
        }
        if (_validationTask != null && !_validationTask.IsCompleted)
        {
            ImGui.SameLine();
            if (_uiShared.IconTextButton(FontAwesomeIcon.Times, "取消"))
            {
                _validationCts?.Cancel();
            }
        }

        if (_validationTask != null)
        {
            using (ImRaii.PushIndent(20f))
            {
                if (_validationTask.IsCompleted)
                {
                    UiSharedService.TextWrapped($"开始文件完整性检查已完成,移除了 {_validationTask.Result.Count} 个有问题的文件.");
                }
                else
                {

                    UiSharedService.TextWrapped($"开始文件完整性检查正在运行: {_currentProgress.Item1}/{_currentProgress.Item2}");
                    UiSharedService.TextWrapped($"正在检查: {_currentProgress.Item3.ResolvedFilepath}");
                }
            }
        }
        ImGui.Separator();

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));
        ImGui.TextUnformatted("清除本地存储前你必须阅读并同意同意以下声明");
        ImGui.Indent();
        ImGui.Checkbox("##readClearCache", ref _readClearCache);
        ImGui.SameLine();
        UiSharedService.TextWrapped("我已了解： " + Environment.NewLine + "- 通过清除本地存储，我不得不重新下载所有数据，从而使连接服务的文件服务器承受了额外的压力。"
            + Environment.NewLine + "- 这不是试图解决同步问题的步骤。"
            + Environment.NewLine + "- 在文件服务器负载繁重的情况下，这可能会使无法获取其他玩家数据的情况变得更糟。");
        if (!_readClearCache)
            ImGui.BeginDisabled();
        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "清除本地存储") && UiSharedService.CtrlPressed() && _readClearCache)
        {
            _ = Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(_configService.Current.CacheFolder))
                {
                    File.Delete(file);
                }
            });
        }
        UiSharedService.AttachToolTip("您通常不需要这样做。为了解决同步问题，您也不应该这样做。" + Environment.NewLine
            + "这将仅删除下载的所有玩家同步的数据，并要求您重新下载所有的内容。" + Environment.NewLine
            + "月海的存储是自动清除的，存储空间不会超过您设置的限制。" + Environment.NewLine
            + "如果你仍然认为你需要这样做，按住CTRL键的同时点击这个按钮。");
        if (!_readClearCache)
            ImGui.EndDisabled();
        ImGui.Unindent();
    }

    private void DrawGeneral()
    {
        if (!string.Equals(_lastTab, "General", StringComparison.OrdinalIgnoreCase))
        {
            _notesSuccessfullyApplied = null;
        }

        _lastTab = "General";
        //_uiShared.BigText("Experimental");
        //ImGui.Separator();

        _uiShared.BigText("备注");
        if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "将所有用户备注导出到剪贴板"))
        {
            ImGui.SetClipboardText(UiSharedService.GetNotes(_pairManager.DirectPairs.UnionBy(_pairManager.GroupPairs.SelectMany(p => p.Value), p => p.UserData, UserDataComparer.Instance).ToList()));
        }
        if (_uiShared.IconTextButton(FontAwesomeIcon.FileImport, "从剪贴板导入备注"))
        {
            _notesSuccessfullyApplied = null;
            var notes = ImGui.GetClipboardText();
            _notesSuccessfullyApplied = _uiShared.ApplyNotesFromClipboard(notes, _overwriteExistingLabels);
        }

        ImGui.SameLine();
        ImGui.Checkbox("覆盖现有备注", ref _overwriteExistingLabels);
        _uiShared.DrawHelpText("如果选择此选项，则导入的备注将覆盖对应UID的所有现存备注。");
        if (_notesSuccessfullyApplied.HasValue && _notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("成功导入用户备注", ImGuiColors.HealerGreen);
        }
        else if (_notesSuccessfullyApplied.HasValue && !_notesSuccessfullyApplied.Value)
        {
            UiSharedService.ColorTextWrapped("尝试从剪贴板导入备注失败，检查格式并重试。", ImGuiColors.DalamudRed);
        }

        var openPopupOnAddition = _configService.Current.OpenPopupOnAdd;

        if (ImGui.Checkbox("添加用户时打开备注菜单", ref openPopupOnAddition))
        {
            _configService.Current.OpenPopupOnAdd = openPopupOnAddition;
            _configService.Save();
        }
        _uiShared.DrawHelpText("这将打开一个弹窗，方便您在成功添加独立配对用户时为其设置备注。");

        var autoPopulateNotes = _configService.Current.AutoPopulateEmptyNotesFromCharaName;
        if (ImGui.Checkbox("自动使用角色名作为角色备注", ref autoPopulateNotes))
        {
            _configService.Current.AutoPopulateEmptyNotesFromCharaName = autoPopulateNotes;
            _configService.Save();
        }
        _uiShared.DrawHelpText("当遇到玩家时, 如果你没有为他/她设置备注, 使用角色名作为备注");

        ImGui.Separator();
        _uiShared.BigText("UI");
        var showNameInsteadOfNotes = _configService.Current.ShowCharacterNameInsteadOfNotesForVisible;
        var showVisibleSeparate = _configService.Current.ShowVisibleUsersSeparately;
        var showOfflineSeparate = _configService.Current.ShowOfflineUsersSeparately;
        var showProfiles = _configService.Current.ProfilesShow;
        var showNsfwProfiles = _configService.Current.ProfilesAllowNsfw;
        var profileDelay = _configService.Current.ProfileDelay;
        var profileOnRight = _configService.Current.ProfilePopoutRight;
        var enableRightClickMenu = _configService.Current.EnableRightClickMenus;
        var enableDtrEntry = _configService.Current.EnableDtrEntry;
        var showUidInDtrTooltip = _configService.Current.ShowUidInDtrTooltip;
        var preferNoteInDtrTooltip = _configService.Current.PreferNoteInDtrTooltip;
        var useColorsInDtr = _configService.Current.UseColorsInDtr;
        var dtrColorsDefault = _configService.Current.DtrColorsDefault;
        var dtrColorsNotConnected = _configService.Current.DtrColorsNotConnected;
        var dtrColorsPairsInRange = _configService.Current.DtrColorsPairsInRange;
        var preferNotesInsteadOfName = _configService.Current.PreferNotesOverNamesForVisible;
        var groupUpSyncshells = _configService.Current.GroupUpSyncshells;
        var groupInVisible = _configService.Current.ShowSyncshellUsersInVisible;
        var syncshellOfflineSeparate = _configService.Current.ShowSyncshellOfflineUsersSeparately;


        var port = _configService.Current.PortToChatGui;

        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedBlue);
        _uiShared.BigText("国服特供");
        ImGui.PopStyleColor();
        using (ImRaii.Disabled(_uiShared.ChatTwoExists))
        {
            if (ImGui.Checkbox("将聊天输出到游戏聊天框", ref port))
            {
                _configService.Current.PortToChatGui = port;
                _configService.Save();
            }
        }
        if (_uiShared.ChatTwoExists)
        {
            UiSharedService.AttachToolTip("已检测到 ChatTwo，聊天将由 ChatTwo 处理，此选项已自动停用以避免重复输出。");
        }

        ImGui.Indent();
        var index = _configService.Current.ChatColor;
        ImGui.TextUnformatted("聊天文字颜色");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo(" -> ##chatcolor",ref index, colors, colors.Length))
        {
            _configService.Current.ChatColor = index;
            _configService.Save();
        }
        unsafe
        {
            var color = uint.Parse(colors[index]);
            var uintColor = raptureAtkModule->AtkUIColorHolder.GetColor(false, color);
            ImGui.SameLine();
            ImGui.TextColored(ColorHelpers.RgbaUintToVector4(uintColor),"大概就是这么个颜色");
        }
        ImGui.Unindent();

        var open = _configService.Current.ShowChatWindowOnLogin;
        if (ImGui.Checkbox("登录时自动打开聊天窗口", ref open))
        {
            _configService.Current.ShowChatWindowOnLogin = open;
            _configService.Save();
        }

        if (ImGui.Button("打开功能介绍"))
        {
            Mediator.Publish(new UiToggleMessage(typeof(ChangelogUi)));
        }

        if (ImGui.Button("加入世界频道(假的)"))
        {
            if (!ChatUi.JoinedGroups.Contains("MSS-GLOBAL"))
            {
                ChatUi.JoinedGroups.Add("MSS-GLOBAL");
                Mediator.Publish(new JoinedGroupsChangedMessage());
            }
        }

        ImGui.Separator();


        if (ImGui.Checkbox("启用游戏右键菜单", ref enableRightClickMenu))
        {
            _configService.Current.EnableRightClickMenus = enableRightClickMenu;
            _configService.Save();
        }
        _uiShared.DrawHelpText("这将在配对玩家的游戏UI中添加与月海相关的右键菜单项。");

        if (ImGui.Checkbox("在服务器信息栏中显示状态和可见配对角色数", ref enableDtrEntry))
        {
            _configService.Current.EnableDtrEntry = enableDtrEntry;
            _configService.Save();
        }
        _uiShared.DrawHelpText("这将在服务器信息栏中添加月海连接状态和可见配对角色数。\n您可以通过Dalamud设置对此进行进一步配置。");

        using (ImRaii.Disabled(!enableDtrEntry))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("在服务器信息提示中显示视野中已配对玩家的UID", ref showUidInDtrTooltip))
            {
                _configService.Current.ShowUidInDtrTooltip = showUidInDtrTooltip;
                _configService.Save();
            }

            if (ImGui.Checkbox("优先显示备注而非UID", ref preferNoteInDtrTooltip))
            {
                _configService.Current.PreferNoteInDtrTooltip = preferNoteInDtrTooltip;
                _configService.Save();
            }

            if (ImGui.Checkbox("对服务器栏的图标染色", ref useColorsInDtr))
            {
                _configService.Current.UseColorsInDtr = useColorsInDtr;
                _configService.Save();
            }

            using (ImRaii.Disabled(!useColorsInDtr))
            {
                using var indent2 = ImRaii.PushIndent();
                if (InputDtrColors("默认", ref dtrColorsDefault))
                {
                    _configService.Current.DtrColorsDefault = dtrColorsDefault;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputDtrColors("未连接", ref dtrColorsNotConnected))
                {
                    _configService.Current.DtrColorsNotConnected = dtrColorsNotConnected;
                    _configService.Save();
                }

                ImGui.SameLine();
                if (InputDtrColors("范围中有配对", ref dtrColorsPairsInRange))
                {
                    _configService.Current.DtrColorsPairsInRange = dtrColorsPairsInRange;
                    _configService.Save();
                }
            }
        }

        if (ImGui.Checkbox("显示单独的“可见”组", ref showVisibleSeparate))
        {
            _configService.Current.ShowVisibleUsersSeparately = showVisibleSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("这将在主界面一个特殊“可见”组中显示所有当前可见的用户。");

        using (ImRaii.Disabled(!showVisibleSeparate))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("在可见组中显示配对贝用户", ref groupInVisible))
            {
                _configService.Current.ShowSyncshellUsersInVisible = groupInVisible;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
        }

        if (ImGui.Checkbox("显示单独的离线组", ref showOfflineSeparate))
        {
            _configService.Current.ShowOfflineUsersSeparately = showOfflineSeparate;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("这将在主界面中一个特殊“离线”组中显示所有当前离线的用户。");

        using (ImRaii.Disabled(!showOfflineSeparate))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("在离线组中显示配对贝用户", ref syncshellOfflineSeparate))
            {
                _configService.Current.ShowSyncshellOfflineUsersSeparately = syncshellOfflineSeparate;
                _configService.Save();
                Mediator.Publish(new RefreshUiMessage());
            }
        }

        if (ImGui.Checkbox("将所有配对贝显示在一个文件夹中", ref groupUpSyncshells))
        {
            _configService.Current.GroupUpSyncshells = groupUpSyncshells;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("这将把所有同步贝移动到主界面的'所有同步贝'文件夹中.");

        if (ImGui.Checkbox("显示可见玩家的玩家名称", ref showNameInsteadOfNotes))
        {
            _configService.Current.ShowCharacterNameInsteadOfNotesForVisible = showNameInsteadOfNotes;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("当角色可见时，这将显示角色名称，而不是自定义备注");

        ImGui.Indent();
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.BeginDisabled();
        if (ImGui.Checkbox("优先显示玩家备注而不是玩家名称", ref preferNotesInsteadOfName))
        {
            _configService.Current.PreferNotesOverNamesForVisible = preferNotesInsteadOfName;
            _configService.Save();
            Mediator.Publish(new RefreshUiMessage());
        }
        _uiShared.DrawHelpText("如果你为玩家设置了一个备注，它将显示出来，而不是玩家的名字");
        if (!_configService.Current.ShowCharacterNameInsteadOfNotesForVisible) ImGui.EndDisabled();
        ImGui.Unindent();

        if (ImGui.Checkbox("在鼠标悬停时显示月海档案", ref showProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesShow = showProfiles;
            _configService.Save();
        }
        _uiShared.DrawHelpText("鼠标悬停一段时间后显示该用户自己设置的月海档案");
        ImGui.Indent();
        if (!showProfiles) ImGui.BeginDisabled();
        if (ImGui.Checkbox("在右侧弹出个人档案", ref profileOnRight))
        {
            _configService.Current.ProfilePopoutRight = profileOnRight;
            _configService.Save();
            Mediator.Publish(new CompactUiChange(Vector2.Zero, Vector2.Zero));
        }
        _uiShared.DrawHelpText("将在主界面的右侧显示档案");
        if (ImGui.SliderFloat("悬停延迟", ref profileDelay, 0.5f, 10,"%.1f"))
        {
            _configService.Current.ProfileDelay = profileDelay;
            _configService.Save();
        }
        _uiShared.DrawHelpText("鼠标悬停多久才显示档案（秒）");
        if (!showProfiles) ImGui.EndDisabled();
        ImGui.Unindent();
        if (ImGui.Checkbox("显示标记为NSFW的档案", ref showNsfwProfiles))
        {
            Mediator.Publish(new ClearProfileDataMessage());
            _configService.Current.ProfilesAllowNsfw = showNsfwProfiles;
            _configService.Save();
        }
        _uiShared.DrawHelpText("将显示启用NSFW标记的档案文件");

        ImGui.Separator();

        var disableOptionalPluginWarnings = _configService.Current.DisableOptionalPluginWarnings;
        var onlineNotifs = _configService.Current.ShowOnlineNotifications;
        var onlineNotifsPairsOnly = _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs;
        var onlineNotifsNamedOnly = _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs;
        _uiShared.BigText("通知");

        _uiShared.DrawCombo("显示 [信息]##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.InfoNotification = i;
            _configService.Save();
        }, _configService.Current.InfoNotification);
        _uiShared.DrawHelpText("显示“信息”通知的位置"
                      + Environment.NewLine + "'Nowhere' 不会显示任何信息通知"
                      + Environment.NewLine + "'Chat' 将在聊天频道中打印信息通知"
                      + Environment.NewLine + "'Toast' 将在右下角显示提示框"
                      + Environment.NewLine + "'Both' 将在聊天频道以及提示框中同时显示");

        _uiShared.DrawCombo("显示 [警告]##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.WarningNotification = i;
            _configService.Save();
        }, _configService.Current.WarningNotification);
        _uiShared.DrawHelpText("显示“警告”通知的位置。"
                              + Environment.NewLine + "'Nowhere' 不会显示任何警告通知"
                              + Environment.NewLine + "'Chat' 将在聊天中打印警告通知"
                              + Environment.NewLine + "'Toast' 将在右下角显示提示框"
                              + Environment.NewLine + "'Both' 将在聊天频道以及提示框中同时显示");

        _uiShared.DrawCombo("显示 [错误]##settingsUi", (NotificationLocation[])Enum.GetValues(typeof(NotificationLocation)), (i) => i.ToString(),
        (i) =>
        {
            _configService.Current.ErrorNotification = i;
            _configService.Save();
        }, _configService.Current.ErrorNotification);
        _uiShared.DrawHelpText("显示“错误”通知的位置。"
                              + Environment.NewLine + "'Nowhere' 不会显示任何错误"
                              + Environment.NewLine + "'Chat' 将在聊天中打印警告错误通知"
                              + Environment.NewLine + "'Toast' 将在右下角显示提示框"
                              + Environment.NewLine + "'Both' 将在聊天频道以及提示框中同时显示");

        if (ImGui.Checkbox("禁用可选插件警告", ref disableOptionalPluginWarnings))
        {
            _configService.Current.DisableOptionalPluginWarnings = disableOptionalPluginWarnings;
            _configService.Save();
        }
        _uiShared.DrawHelpText("启用此选项将不会显示任何丢失可选插件的“警告”消息。");
        if (ImGui.Checkbox("启用上线通知", ref onlineNotifs))
        {
            _configService.Current.ShowOnlineNotifications = onlineNotifs;
            _configService.Save();
        }
        _uiShared.DrawHelpText("启用此选项将在配对用户上线时在右下角显示一个小通知（类型：信息）。");

        using var disabled = ImRaii.Disabled(!onlineNotifs);
        if (ImGui.Checkbox("仅针对独立配对通知", ref onlineNotifsPairsOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForIndividualPairs = onlineNotifsPairsOnly;
            _configService.Save();
        }
        _uiShared.DrawHelpText("启用此选项将仅显示独立配对用户的上线通知（类型：信息）。");
        if (ImGui.Checkbox("仅针对单独备注通知", ref onlineNotifsNamedOnly))
        {
            _configService.Current.ShowOnlineNotificationsOnlyForNamedPairs = onlineNotifsNamedOnly;
            _configService.Save();
        }
        _uiShared.DrawHelpText("启用此选项将仅显示您设置了单独备注配对用户的上线通知（类型：信息）.");
    }

    private void DrawPerformance()
    {
        _uiShared.BigText("性能设置");
        UiSharedService.TextWrapped("此设置将在配对角色可能对你造成较大的性能影响时为你提供更多信息或自动暂停配对.");
        ImGui.Dummy(new Vector2(10));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(10));
        bool showPerformanceIndicator = _playerPerformanceConfigService.Current.ShowPerformanceIndicator;
        if (ImGui.Checkbox("显示性能提示", ref showPerformanceIndicator))
        {
            _playerPerformanceConfigService.Current.ShowPerformanceIndicator = showPerformanceIndicator;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("当配对对象超过了你设置的性能限制时将在MareUI上添加一个提示." + Environment.NewLine + "将使用警告等级的设置.");
        bool warnOnExceedingThresholds = _playerPerformanceConfigService.Current.WarnOnExceedingThresholds;
        if (ImGui.Checkbox("当载入的角色超过了你设置的性能限制时显示警告", ref warnOnExceedingThresholds))
        {
            _playerPerformanceConfigService.Current.WarnOnExceedingThresholds = warnOnExceedingThresholds;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("将在该角色被载入时显示该警告. 你进行过独立设置的角色不会触发警告.");
        using (ImRaii.Disabled(!warnOnExceedingThresholds && !showPerformanceIndicator))
        {
            using var indent = ImRaii.PushIndent();
            var warnOnPref = _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds;
            if (ImGui.Checkbox("进行过独立设置的角色也显示提示/警告", ref warnOnPref))
            {
                _playerPerformanceConfigService.Current.WarnOnPreferredPermissionsExceedingThresholds = warnOnPref;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("Mare将对你进行过独立设置的角色也显示提示/警告. 如果警告被设置为关闭,本选项将不会生效.");
        }
        using (ImRaii.Disabled(!showPerformanceIndicator && !warnOnExceedingThresholds))
        {
            var vram = _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB;
            var tris = _playerPerformanceConfigService.Current.TrisWarningThresholdThousands;
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("显存占用限制·警告", ref vram))
            {
                _playerPerformanceConfigService.Current.VRAMSizeWarningThresholdMiB = vram;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(MiB)");
            _uiShared.DrawHelpText("设置对于显存占用显示提示和警告的限制." + UiSharedService.TooltipSeparator
                + "默认: 375 MiB");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("模型面数限制·警告", ref tris))
            {
                _playerPerformanceConfigService.Current.TrisWarningThresholdThousands = tris;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(K 面)");
            _uiShared.DrawHelpText("设置对于模型面数显示提示和警告的限制." + UiSharedService.TooltipSeparator
                + "默认: 165 K");
        }
        ImGui.Dummy(new Vector2(10));
        bool autoPause = _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds;
        bool autoPauseEveryone = _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds;
        if (ImGui.Checkbox("自动暂停与超过限制的角色的配对", ref autoPause))
        {
            _playerPerformanceConfigService.Current.AutoPausePlayersExceedingThresholds = autoPause;
            _playerPerformanceConfigService.Save();
        }
        _uiShared.DrawHelpText("启用时,将自动暂停与超过了下方设置的限制的角色的同步." + Environment.NewLine
            + "同时将在聊天框中输出提示."
            + UiSharedService.TooltipSeparator + "警告: 你必须手动开启同步, Mare不会自动解除暂停状态.");
        using (ImRaii.Disabled(!autoPause))
        {
            using var indent = ImRaii.PushIndent();
            if (ImGui.Checkbox("自动暂停也对你进行了独立设置的角色生效", ref autoPauseEveryone))
            {
                _playerPerformanceConfigService.Current.AutoPausePlayersWithPreferredPermissionsExceedingThresholds = autoPauseEveryone;
                _playerPerformanceConfigService.Save();
            }
            _uiShared.DrawHelpText("启用时, 将对你进行过独立设置的角色也进行自动暂停." + UiSharedService.TooltipSeparator +
                "警告: 你必须手动开启同步, Mare不会自动解除暂停状态.");
            var vramAuto = _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB;
            var trisAuto = _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands;
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("显存占用限制·自动暂停", ref vramAuto))
            {
                _playerPerformanceConfigService.Current.VRAMSizeAutoPauseThresholdMiB = vramAuto;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(MiB)");
            _uiShared.DrawHelpText("当角色显存占用超过了以下限制时, 自动暂停与他们的配对." + UiSharedService.TooltipSeparator
                + "默认: 550 MiB");
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("模型面数限制·自动暂停", ref trisAuto))
            {
                _playerPerformanceConfigService.Current.TrisAutoPauseThresholdThousands = trisAuto;
                _playerPerformanceConfigService.Save();
            }
            ImGui.SameLine();
            ImGui.Text("(K 面)");
            _uiShared.DrawHelpText("当角色模型面数超过了以下限制时, 自动暂停与他们的配对." + UiSharedService.TooltipSeparator
                + "默认: 250 K");
        }
        ImGui.Dummy(new Vector2(10));
        _uiShared.BigText("白名单(UID)");
        UiSharedService.TextWrapped("白名单中的角色不会被自动暂停.");
        ImGui.Dummy(new Vector2(10));
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        ImGui.InputText("##ignoreuid", ref _uidToAddForIgnore, 20);
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_uidToAddForIgnore)))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "将UID/个性UID添加到白名单"))
            {
                if (!_playerPerformanceConfigService.Current.UIDsToIgnore.Contains(_uidToAddForIgnore, StringComparer.Ordinal))
                {
                    _playerPerformanceConfigService.Current.UIDsToIgnore.Add(_uidToAddForIgnore);
                    _playerPerformanceConfigService.Save();
                }
                _uidToAddForIgnore = string.Empty;
            }
        }
        _uiShared.DrawHelpText("提示: UIDs 大小写敏感.");
        var playerList = _playerPerformanceConfigService.Current.UIDsToIgnore;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        using (var lb = ImRaii.ListBox("UID 白名单"))
        {
            if (lb)
            {
                for (int i = 0; i < playerList.Count; i++)
                {
                    bool shouldBeSelected = _selectedEntry == i;
                    if (ImGui.Selectable(playerList[i] + "##" + i, shouldBeSelected))
                    {
                        _selectedEntry = i;
                    }
                }
            }
        }
        using (ImRaii.Disabled(_selectedEntry == -1))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "删除选中的 UID"))
            {
                _playerPerformanceConfigService.Current.UIDsToIgnore.RemoveAt(_selectedEntry);
                _selectedEntry = -1;
                _playerPerformanceConfigService.Save();
            }
        }
    }

    private void DrawServerConfiguration()
    {
        _lastTab = "服务设置";
        if (ApiController.ServerAlive)
        {
            _uiShared.BigText("服务操作");
            ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
            if (ImGui.Button("删除我的所有文件"))
            {
                _deleteFilesPopupModalShown = true;
                ImGui.OpenPopup("是否删除所有文件？");
            }

            _uiShared.DrawHelpText("完全删除您上传到该服务上的所有文件。");

            if (ImGui.BeginPopupModal("是否删除所有文件？", ref _deleteFilesPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "您上传到该服务上的所有文件都将被删除。\n此操作无法撤消。");
                ImGui.TextUnformatted("确定要继续吗?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                 ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("删除所有内容", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(_fileTransferManager.DeleteAllFiles);
                    _deleteFilesPopupModalShown = false;
                }

                ImGui.SameLine();

                if (ImGui.Button("取消##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteFilesPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("删除帐户"))
            {
                _deleteAccountPopupModalShown = true;
                ImGui.OpenPopup("删除您的帐户？");
            }

            _uiShared.DrawHelpText("完全删除您的帐户和所有上传到该服务的文件。");

            if (ImGui.BeginPopupModal("删除您的帐户？", ref _deleteAccountPopupModalShown, UiSharedService.PopupWindowFlags))
            {
                UiSharedService.TextWrapped(
                    "您的帐户以及服务上的所有相关文件和数据都将被删除.");
                UiSharedService.TextWrapped("您的UID将从所有配对列表中删除.");
                ImGui.TextUnformatted("确定要继续吗?");
                ImGui.Separator();
                ImGui.Spacing();

                var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X -
                                  ImGui.GetStyle().ItemSpacing.X) / 2;

                if (ImGui.Button("删除帐户", new Vector2(buttonSize, 0)))
                {
                    _ = Task.Run(ApiController.UserDelete);
                    _deleteAccountPopupModalShown = false;
                    Mediator.Publish(new SwitchToIntroUiMessage());
                }

                ImGui.SameLine();

                if (ImGui.Button("取消##cancelDelete", new Vector2(buttonSize, 0)))
                {
                    _deleteAccountPopupModalShown = false;
                }

                UiSharedService.SetScaledWindowSize(325);
                ImGui.EndPopup();
            }
            ImGui.Separator();
        }

        _uiShared.BigText("服务和角色设置");
        ImGuiHelpers.ScaledDummy(new Vector2(5, 5));
        var sendCensus = _serverConfigurationManager.SendCensusData;
        if (ImGui.Checkbox("发送角色普查数据", ref sendCensus))
        {
            _serverConfigurationManager.SendCensusData = sendCensus;
        }
        _uiShared.DrawHelpText("您将发送以下数据到当前连接的服务器." + UiSharedService.TooltipSeparator
            + "数据包括:" + Environment.NewLine
            + "- 当前服务器" + Environment.NewLine
            + "- 当前角色性别" + Environment.NewLine
            + "- 当前角色种族" + Environment.NewLine
            + "- 当前角色氏族 (如:晨曦之民 或 中原之民)" + UiSharedService.TooltipSeparator
            + "这些数据仅会进行短期保存并在你断开与服务器的连接时删除. 这些数据会临时与你的UID绑定." + UiSharedService.TooltipSeparator
            + "如果你不想参与角色普查, 取消选中复选框并重新连接服务器.");
        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var idx = _uiShared.DrawServiceSelection();
        if (_lastSelectedServerIndex != idx)
        {
            _uiShared.ResetOAuthTasksState();
            _secretKeysConversionCts = _secretKeysConversionCts.CancelRecreate();
            _secretKeysConversionTask = null;
            _lastSelectedServerIndex = idx;
        }

        ImGuiHelpers.ScaledDummy(new Vector2(10, 10));

        var selectedServer = _serverConfigurationManager.GetServerByIndex(idx);
        if (selectedServer == _serverConfigurationManager.CurrentServer)
        {
            UiSharedService.ColorTextWrapped("要将任何修改应用到当前服务，您需要重新连接到该服务。", ImGuiColors.DalamudYellow);
        }

        bool useOauth = selectedServer.UseOAuth2;

        if (ImGui.BeginTabBar("serverTabBar"))
        {
            if (ImGui.BeginTabItem("角色管理"))
            {
                if (selectedServer.SecretKeys.Any() || useOauth)
                {
                    UiSharedService.ColorTextWrapped("此处列出的角色将使用下面提供的设置自动连接到选定的月海服务。" +
                        " 请确保输入正确的角色名称或使用底部的“添加当前角色”按钮。", ImGuiColors.DalamudYellow);
                    int i = 0;
                    _uiShared.DrawUpdateOAuthUIDsButton(selectedServer);

                    if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken))
                    {
                        bool hasSetSecretKeysButNoUid = selectedServer.Authentications.Exists(u => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID));
                        if (hasSetSecretKeysButNoUid)
                        {
                            ImGui.Dummy(new(5f, 5f));
                            UiSharedService.TextWrapped("检测到部分角色已分配密钥但未设置UID. " +
                                "点击下方按钮来自动分配.");
                            using (ImRaii.Disabled(_secretKeysConversionTask != null && !_secretKeysConversionTask.IsCompleted))
                            {
                                if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowsLeftRight, "转换密钥到UID"))
                                {
                                    _secretKeysConversionTask = ConvertSecretKeysToUIDs(selectedServer, _secretKeysConversionCts.Token);
                                }
                            }
                            if (_secretKeysConversionTask != null && !_secretKeysConversionTask.IsCompleted)
                            {
                                UiSharedService.ColorTextWrapped("正在转换密钥到UID", ImGuiColors.DalamudYellow);
                            }
                            if (_secretKeysConversionTask != null && _secretKeysConversionTask.IsCompletedSuccessfully)
                            {
                                Vector4? textColor = null;
                                if (_secretKeysConversionTask.Result.PartialSuccess)
                                {
                                    textColor = ImGuiColors.DalamudYellow;
                                }
                                if (!_secretKeysConversionTask.Result.Success)
                                {
                                    textColor = ImGuiColors.DalamudRed;
                                }
                                string text = $"转换完成: {_secretKeysConversionTask.Result.Result}";
                                if (textColor == null)
                                {
                                    UiSharedService.TextWrapped(text);
                                }
                                else
                                {
                                    UiSharedService.ColorTextWrapped(text, textColor!.Value);
                                }
                                if (!_secretKeysConversionTask.Result.Success || _secretKeysConversionTask.Result.PartialSuccess)
                                {
                                    UiSharedService.TextWrapped("部分密钥转换失败, 请手工分配.");
                                }
                            }
                        }
                    }
                    ImGui.Separator();
                    string youName = _dalamudUtilService.GetPlayerName();
                    uint youWorld = _dalamudUtilService.GetHomeWorldId();
                    ulong youCid = _dalamudUtilService.GetCID();
                    if (!selectedServer.Authentications.Exists(a => string.Equals(a.CharacterName, youName, StringComparison.Ordinal) && a.WorldId == youWorld))
                    {
                        _uiShared.BigText("你尚未配置角色", ImGuiColors.DalamudRed);
                        UiSharedService.ColorTextWrapped("你尚未为你正在使用的角色做好配置.", ImGuiColors.DalamudRed);
                        var authWithCid = selectedServer.Authentications.Find(f => f.LastSeenCID == youCid);
                        if (authWithCid != null)
                        {
                            ImGuiHelpers.ScaledDummy(5);
                            UiSharedService.ColorText("你可能改名/转区了:", ImGuiColors.DalamudYellow);
                            using (ImRaii.PushIndent(10f))
                                UiSharedService.ColorText("条目: " + authWithCid.CharacterName + " - " + _dalamudUtilService.WorldData.Value[(ushort)authWithCid.WorldId], ImGuiColors.ParsedGreen);
                            UiSharedService.ColorText("点击下方按钮为当前角色使用这一条目:", ImGuiColors.DalamudYellow);
                            using (ImRaii.PushIndent(10f))
                                UiSharedService.ColorText("当前: " + youName + " - " + _dalamudUtilService.WorldData.Value[(ushort)youWorld], ImGuiColors.ParsedGreen);
                            ImGuiHelpers.ScaledDummy(5);
                            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowRight, "更新条目为当前角色信息"))
                            {
                                authWithCid.CharacterName = youName;
                                authWithCid.WorldId = youWorld;
                                _serverConfigurationManager.Save();
                            }
                        }
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.Separator();
                        ImGuiHelpers.ScaledDummy(5);
                    }
                    foreach (var item in selectedServer.Authentications.ToList())
                    {
                        using var charaId = ImRaii.PushId("selectedChara" + i);

                        var worldIdx = (ushort)item.WorldId;
                        var data = _uiShared.WorldData.OrderBy(u => u.Value, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
                        if (!data.TryGetValue(worldIdx, out string? worldPreview))
                        {
                            worldPreview = data.First().Value;
                        }

                        Dictionary<int, SecretKey> keys = [];

                        if (!useOauth)
                        {
                            var secretKeyIdx = item.SecretKeyIdx;
                            keys = selectedServer.SecretKeys;
                            if (!keys.TryGetValue(secretKeyIdx, out var secretKey))
                            {
                                secretKey = new();
                            }
                        }

                        bool thisIsYou = false;
                        if (string.Equals(youName, item.CharacterName, StringComparison.OrdinalIgnoreCase)
                            && youWorld == worldIdx)
                        {
                            thisIsYou = true;
                        }
                        bool misManaged = false;
                        if (selectedServer.UseOAuth2 && !string.IsNullOrEmpty(selectedServer.OAuthToken) && string.IsNullOrEmpty(item.UID))
                        {
                            misManaged = true;
                        }
                        if (!selectedServer.UseOAuth2 && item.SecretKeyIdx == -1)
                        {
                            misManaged = true;
                        }
                        Vector4 color = ImGuiColors.ParsedGreen;
                        string text = thisIsYou ? "当前角色" : string.Empty;
                        if (misManaged)
                        {
                            text += " [配置错误 (" + (selectedServer.UseOAuth2 ? "未设置UID" : "未设置密钥") + ")]";
                            color = ImGuiColors.DalamudRed;
                        }
                        if (selectedServer.Authentications.Where(e => e != item).Any(e => string.Equals(e.CharacterName, item.CharacterName, StringComparison.Ordinal)
                            && e.WorldId == item.WorldId))
                        {
                            text += " [弃用]";
                            color = ImGuiColors.DalamudRed;
                        }

                        if (!string.IsNullOrEmpty(text))
                        {
                            text = text.Trim();
                            _uiShared.BigText(text, color);
                        }

                        var charaName = item.CharacterName;
                        if (ImGui.InputText("角色名", ref charaName, 64))
                        {
                            item.CharacterName = charaName;
                            _serverConfigurationManager.Save();
                        }

                        _uiShared.DrawCombo("服务器##" + item.CharacterName + i, data, (w) => w.Value,
                            (w) =>
                            {
                                if (item.WorldId != w.Key)
                                {
                                    item.WorldId = w.Key;
                                    _serverConfigurationManager.Save();
                                }
                            }, EqualityComparer<KeyValuePair<ushort, string>>.Default.Equals(data.FirstOrDefault(f => f.Key == worldIdx), default) ? data.First() : data.First(f => f.Key == worldIdx));

                        if (!useOauth)
                        {
                            _uiShared.DrawCombo("密钥###" + item.CharacterName + i, keys, (w) => w.Value.FriendlyName,
                                (w) =>
                                {
                                    if (w.Key != item.SecretKeyIdx)
                                    {
                                        item.SecretKeyIdx = w.Key;
                                        _serverConfigurationManager.Save();
                                    }
                                }, EqualityComparer<KeyValuePair<int, SecretKey>>.Default.Equals(keys.FirstOrDefault(f => f.Key == item.SecretKeyIdx), default) ? keys.First() : keys.First(f => f.Key == item.SecretKeyIdx));
                        }
                        else
                        {
                            _uiShared.DrawUIDComboForAuthentication(i, item, selectedServer.ServerUri, _logger);
                        }
                        bool isAutoLogin = item.AutoLogin;
                        if (ImGui.Checkbox("自动登录", ref isAutoLogin))
                        {
                            item.AutoLogin = isAutoLogin;
                            _serverConfigurationManager.Save();
                        }
                        _uiShared.DrawHelpText("启用后, 当你登录后会自动连接到Mare服务器.");
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "删除角色") && UiSharedService.CtrlPressed())
                            _serverConfigurationManager.RemoveCharacterFromServer(idx, item);
                        UiSharedService.AttachToolTip("按住CTRL并点击以删除.");

                        i++;
                        if (item != selectedServer.Authentications.ToList()[^1])
                        {
                            ImGuiHelpers.ScaledDummy(5);
                            ImGui.Separator();
                            ImGuiHelpers.ScaledDummy(5);
                        }
                    }

                    if (selectedServer.Authentications.Any())
                        ImGui.Separator();

                    if (!selectedServer.Authentications.Exists(c => string.Equals(c.CharacterName, youName, StringComparison.Ordinal)
                        && c.WorldId == youWorld))
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.User, "添加当前角色"))
                        {
                            _serverConfigurationManager.AddCurrentCharacterToServer(idx);
                        }
                        ImGui.SameLine();
                    }

                    if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "添加新角色"))
                    {
                        _serverConfigurationManager.AddEmptyCharacterToServer(idx);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("先添加密钥，再添加角色。", ImGuiColors.DalamudYellow);
                }

                ImGui.EndTabItem();
            }

            if (!useOauth && ImGui.BeginTabItem("密钥管理"))
            {
                foreach (var item in selectedServer.SecretKeys.ToList())
                {
                    using var id = ImRaii.PushId("key" + item.Key);
                    var friendlyName = item.Value.FriendlyName;
                    if (ImGui.InputText("密钥显示名称", ref friendlyName, 255))
                    {
                        item.Value.FriendlyName = friendlyName;
                        _serverConfigurationManager.Save();
                    }
                    var key = item.Value.Key;
                    if (ImGui.InputText("密钥", ref key, 64))
                    {
                        item.Value.Key = key;
                        _serverConfigurationManager.Save();
                    }
                    if (!selectedServer.Authentications.Exists(p => p.SecretKeyIdx == item.Key))
                    {
                        if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "删除密钥") && UiSharedService.CtrlPressed())
                        {
                            selectedServer.SecretKeys.Remove(item.Key);
                            _serverConfigurationManager.Save();
                        }
                        UiSharedService.AttachToolTip("按住CTRL键可删除此密钥项");
                    }
                    else
                    {
                        UiSharedService.ColorTextWrapped("此密钥正在使用，无法删除", ImGuiColors.DalamudYellow);
                    }

                    if (item.Key != selectedServer.SecretKeys.Keys.LastOrDefault())
                        ImGui.Separator();
                }

                ImGui.Separator();
                if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "添加新密钥"))
                {
                    selectedServer.SecretKeys.Add(selectedServer.SecretKeys.Any() ? selectedServer.SecretKeys.Max(p => p.Key) + 1 : 0, new SecretKey()
                    {
                        FriendlyName = "新密钥",
                    });
                    _serverConfigurationManager.Save();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("服务器设置"))
            {
                var serverName = selectedServer.ServerName;
                var serverUri = selectedServer.ServerUri;
                var isMain = string.Equals(serverName, ApiController.MainServer, StringComparison.OrdinalIgnoreCase);
                var flags = isMain ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None;

                if (ImGui.InputText("服务器URI", ref serverUri, 255, flags))
                {
                    selectedServer.ServerUri = serverUri;
                }
                if (isMain)
                {
                    _uiShared.DrawHelpText("无法编辑主服务器的URI。");
                }

                if (ImGui.InputText("服务器名称", ref serverName, 255, flags))
                {
                    selectedServer.ServerName = serverName;
                    _serverConfigurationManager.Save();
                }
                if (isMain)
                {
                    _uiShared.DrawHelpText("无法编辑主服务的名称。");
                }

                ImGui.SetNextItemWidth(200);
                var serverTransport = _serverConfigurationManager.GetTransport();
                _uiShared.DrawCombo("服务器传输方式", Enum.GetValues<HttpTransportType>().Where(t => t != HttpTransportType.None),
                    (v) => v.ToString(),
                    onSelected: (t) => _serverConfigurationManager.SetTransportType(t),
                    serverTransport);
                _uiShared.DrawHelpText("你一般不应该切换这个选项, 如果你不知道这是什么东西, 别改." + Environment.NewLine
                    + "如果你使用VPN或其他网络工具出现问题, 先试试ServerSentEvents然后才是LongPolling." + UiSharedService.TooltipSeparator
                    + "注意: 如果服务器不支持, 会按照以下顺序回退: WebSockets > ServerSentEvents > LongPolling");

                if (_dalamudUtilService.IsWine)
                {
                    bool forceWebSockets = selectedServer.ForceWebSockets;
                    if (ImGui.Checkbox("[仅wine] 强制使用 WebSockets", ref forceWebSockets))
                    {
                        selectedServer.ForceWebSockets = forceWebSockets;
                        _serverConfigurationManager.Save();
                    }
                    _uiShared.DrawHelpText("wine环境下, Mare将自动回退至 ServerSentEvents/LongPolling. "
                        + "WebSockets 在 wine 8.5 环境下会使FF崩溃. "
                        + "请在你未使用 wine 8.5时选用本选项." + Environment.NewLine
                        + "注意: 如果未来的某个时间问题被解决,本选项将被移除.");
                }

                ImGuiHelpers.ScaledDummy(5);

                if (ImGui.Checkbox("使用 Discord OAuth2 认证", ref useOauth))
                {
                    selectedServer.UseOAuth2 = useOauth;
                    _serverConfigurationManager.Save();
                }
                _uiShared.DrawHelpText("使用 Discord OAuth2 而非密钥登录来服务器");
                if (useOauth)
                {
                    _uiShared.DrawOAuth(selectedServer);
                    if (string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)))
                    {
                        ImGuiHelpers.ScaledDummy(10f);
                        UiSharedService.ColorTextWrapped("你已经启用了OAuth2但未关联Discord账户. 点击按钮以检查, 之后进行授权.", ImGuiColors.DalamudRed);
                    }
                    if (!string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer))
                        && selectedServer.Authentications.TrueForAll(u => string.IsNullOrEmpty(u.UID)))
                    {
                        ImGuiHelpers.ScaledDummy(10f);
                        UiSharedService.ColorTextWrapped("你已经启用了OAuth2登录, 但并未为当前角色分配UID. 请在 \"角色管理\"中进行分配.",
                            ImGuiColors.DalamudRed);
                    }
                }

                if (!isMain && selectedServer != _serverConfigurationManager.CurrentServer)
                {
                    ImGui.Separator();
                    if (_uiShared.IconTextButton(FontAwesomeIcon.Trash, "删除服务") && UiSharedService.CtrlPressed())
                    {
                        _serverConfigurationManager.DeleteServer(selectedServer);
                    }
                    _uiShared.DrawHelpText("按住CTRL键可删除此服务");
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("权限设置"))
            {
                _uiShared.BigText("默认权限设置");
                if (selectedServer == _serverConfigurationManager.CurrentServer && _apiController.IsConnected)
                {
                    UiSharedService.TextWrapped("注意: 默认权限设置对已有的独立配对和配对贝不生效(仅对新配对生效).");
                    UiSharedService.TextWrapped("注意: 默认权限设置将被发送并储存在连接到的服务器上.");
                    ImGuiHelpers.ScaledDummy(5f);
                    var perms = _apiController.DefaultPermissions!;
                    bool individualIsSticky = perms.IndividualIsSticky;
                    bool disableIndividualSounds = perms.DisableIndividualSounds;
                    bool disableIndividualAnimations = perms.DisableIndividualAnimations;
                    bool disableIndividualVFX = perms.DisableIndividualVFX;
                    if (ImGui.Checkbox("对特定用户的同步设置优先于配对贝设置生效", ref individualIsSticky))
                    {
                        perms.IndividualIsSticky = individualIsSticky;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("优先生效是指:你对贝中用户进行的单独设置优先于你对配对贝的设置生效" +
                        "(例如: 你暂停了与配对贝中一个玩家的动画同步, 之后暂停了这个贝的整体同步并再次开启同步, 该用户会保持在暂停同步的状态 - " +
                        "剩余没有被单独设定过的用户会遵从配对贝的设定与你同步)." + Environment.NewLine + Environment.NewLine +
                        "请注意:" + Environment.NewLine +
                        "  - 所有新的独立配对也会遵从本设定." + Environment.NewLine +
                        "  - 对*任何单体*配对进行的同步设置修改(包括对贝中某些玩家进行的修改)会成为之后的默认设置." + Environment.NewLine + Environment.NewLine +
                        "你可以随时开启或关闭本功能." + Environment.NewLine + Environment.NewLine +
                        "如果对本设置有疑问,请勿打开本设置.");
                    ImGuiHelpers.ScaledDummy(3f);

                    if (ImGui.Checkbox("关闭独立配对的声音同步", ref disableIndividualSounds))
                    {
                        perms.DisableIndividualSounds = disableIndividualSounds;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("将默认关闭所有新独立配对的声音同步功能.");
                    if (ImGui.Checkbox("关闭独立配对的动画同步", ref disableIndividualAnimations))
                    {
                        perms.DisableIndividualAnimations = disableIndividualAnimations;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("将默认关闭所有新独立配对的动画同步功能.");
                    if (ImGui.Checkbox("关闭独立配对的VFX同步", ref disableIndividualVFX))
                    {
                        perms.DisableIndividualVFX = disableIndividualVFX;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("将默认关闭所有新独立配对的VFX同步功能.");
                    ImGuiHelpers.ScaledDummy(5f);
                    bool disableGroundSounds = perms.DisableGroupSounds;
                    bool disableGroupAnimations = perms.DisableGroupAnimations;
                    bool disableGroupVFX = perms.DisableGroupVFX;
                    if (ImGui.Checkbox("关闭配对贝的声音同步", ref disableGroundSounds))
                    {
                        perms.DisableGroupSounds = disableGroundSounds;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("将默认关闭所有新加入的同步贝的声音同步功能(被单独设定的用户不受影响).");
                    if (ImGui.Checkbox("关闭配对贝的动画同步", ref disableGroupAnimations))
                    {
                        perms.DisableGroupAnimations = disableGroupAnimations;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("将默认关闭所有新加入的同步贝的动画同步功能(被单独设定的用户不受影响).");
                    if (ImGui.Checkbox("关闭配对贝的VFX同步", ref disableGroupVFX))
                    {
                        perms.DisableGroupVFX = disableGroupVFX;
                        _ = _apiController.UserUpdateDefaultPermissions(perms);
                    }
                    _uiShared.DrawHelpText("将默认关闭所有新加入的同步贝的VFX同步功能(被单独设定的用户不受影响).");
                }
                else
                {
                    UiSharedService.ColorTextWrapped("暂时无法获取默认设置. " +
                        "你需要先连接到该服务器才能修改默认设置.", ImGuiColors.DalamudYellow);
                }

                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }



    private int _lastSelectedServerIndex = -1;
    private Task<(bool Success, bool PartialSuccess, string Result)>? _secretKeysConversionTask = null;
    private CancellationTokenSource _secretKeysConversionCts = new CancellationTokenSource();

    private async Task<(bool Success, bool partialSuccess, string Result)> ConvertSecretKeysToUIDs(ServerStorage serverStorage, CancellationToken token)
    {
        List<Authentication> failedConversions = serverStorage.Authentications.Where(u => u.SecretKeyIdx == -1 && string.IsNullOrEmpty(u.UID)).ToList();
        List<Authentication> conversionsToAttempt = serverStorage.Authentications.Where(u => u.SecretKeyIdx != -1 && string.IsNullOrEmpty(u.UID)).ToList();
        List<Authentication> successfulConversions = [];
        Dictionary<string, List<Authentication>> secretKeyMapping = new(StringComparer.Ordinal);
        foreach (var authEntry in conversionsToAttempt)
        {
            if (!serverStorage.SecretKeys.TryGetValue(authEntry.SecretKeyIdx, out var secretKey))
            {
                failedConversions.Add(authEntry);
                continue;
            }

            if (!secretKeyMapping.TryGetValue(secretKey.Key, out List<Authentication>? authList))
            {
                secretKeyMapping[secretKey.Key] = authList = [];
            }

            authList.Add(authEntry);
        }

        if (secretKeyMapping.Count == 0)
        {
            return (false, false, $"{failedConversions.Count} 个条目转换失败: " + string.Join(", ", failedConversions.Select(k => k.CharacterName)));
        }

        var baseUri = serverStorage.ServerUri.Replace("wss://", "https://").Replace("ws://", "http://");
        var oauthCheckUri = MareAuth.GetUIDsBasedOnSecretKeyFullPath(new Uri(baseUri));
        var requestContent = JsonContent.Create(secretKeyMapping.Select(k => k.Key).ToList());
        HttpRequestMessage requestMessage = new(HttpMethod.Post, oauthCheckUri);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serverStorage.OAuthToken);
        requestMessage.Content = requestContent;

        using var response = await _httpClient.SendAsync(requestMessage, token).ConfigureAwait(false);
        Dictionary<string, string>? secretKeyUidMapping = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>
            (await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
        if (secretKeyUidMapping == null)
        {
            return (false, false, $"获取数据失败, 没有进行转换.");
        }

        foreach (var entry in secretKeyMapping)
        {
            if (!secretKeyUidMapping.TryGetValue(entry.Key, out var assignedUid) || string.IsNullOrEmpty(assignedUid))
            {
                failedConversions.AddRange(entry.Value);
                continue;
            }

            foreach (var auth in entry.Value)
            {
                auth.UID = assignedUid;
                successfulConversions.Add(auth);
            }
        }

        if (successfulConversions.Count > 0)
            _serverConfigurationManager.Save();

        StringBuilder sb = new();
        sb.Append("转换结束." + Environment.NewLine);
        sb.Append($"成功转换了 {successfulConversions.Count} 个条目." + Environment.NewLine);
        if (failedConversions.Count > 0)
        {
            sb.Append($"转换失败 {failedConversions.Count} 个条目, 请手动分配: ");
            sb.Append(string.Join(", ", failedConversions.Select(k => k.CharacterName)));
        }

        return (true, failedConversions.Count != 0, sb.ToString());
    }

    private void DrawSettingsContent()
    {
        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.TextUnformatted("服务器 " + _serverConfigurationManager.CurrentServer!.ServerName + ":");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, "在线");
            ImGui.SameLine();
            ImGui.TextUnformatted("(");
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture));
            ImGui.SameLine();
            ImGui.TextUnformatted("用户在线");
            ImGui.SameLine();
            ImGui.TextUnformatted(")");
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("社区和支持（国服的）:");
        ImGui.SameLine();
        if (ImGui.Button("月海同步器/Mare Synchronos Discord"))
        {
            Util.OpenLink("https://discord.gg/usCnZFhEHs");
        }
        ImGui.Separator();
        if (ImGui.BeginTabBar("mainTabBar"))
        {
            if (ImGui.BeginTabItem("常规设置"))
            {
                DrawGeneral();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("性能"))
            {
                DrawPerformance();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("存储"))
            {
                DrawFileStorageSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("传输设置"))
            {
                DrawCurrentTransfers();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("服务设置"))
            {
                DrawServerConfiguration();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("调试"))
            {
                DrawDebug();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}