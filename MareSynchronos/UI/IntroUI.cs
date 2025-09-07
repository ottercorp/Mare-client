using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.FileCache;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class IntroUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly Dictionary<string, string> _languages = new(StringComparer.Ordinal) { { "English", "en" }, { "Deutsch", "de" }, { "Français", "fr" }, { "中文", "zh"  }};
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly UiSharedService _uiShared;
    private int _currentLanguage;
    private bool _readFirstPage;

    private string _secretKey = string.Empty;
    private string _timeoutLabel = string.Empty;
    private Task? _timeoutTask;
    private string[]? _tosParagraphs;
    private bool _useLegacyLogin = false;

    public IntroUi(ILogger<IntroUi> logger, UiSharedService uiShared, MareConfigService configService,
        CacheMonitor fileCacheManager, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator,
        PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService) : base(logger, mareMediator, "Mare Synchronos/月海同步器设置", performanceCollectorService)
    {
        _uiShared = uiShared;
        _configService = configService;
        _cacheMonitor = fileCacheManager;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(600, 2000),
        };

        GetToSLocalization();

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) =>
        {
            _configService.Current.UseCompactor = !dalamudUtilService.IsWine;
            IsOpen = true;
        });
    }

    private int _prevIdx = -1;

    protected override void DrawInternal()
    {
        if (_uiShared.IsInGpose) return;

        if (!_configService.Current.AcceptedAgreement && !_readFirstPage)
        {
            _uiShared.BigText("欢迎使用月海同步器");
            ImGui.Separator();
            UiSharedService.TextWrapped("月海同步器可以将您的角色当前的完整状态同步给与您配对的其他用户，包括Penumbra的模组。" +
                              "请注意，您必须安装Penumbra和Glamourer才能使用此插件。");
            UiSharedService.TextWrapped("在您开始使用这个插件之前，我们必须先设置一些东西。单击“下一步”继续。");

            UiSharedService.ColorTextWrapped("注意：除了Penumbra，您通过其他任何方式应用的修改都不能被同步。" +
                                 "受此影响，您在他人客户端上的角色状态可能会因此看起来不完整。" +
                                 "如果打算继续使用此插件，您需要将所有模组都转移到Penumbra。", ImGuiColors.DalamudYellow);
            if (!_uiShared.DrawOtherPluginState()) return;
            ImGui.Separator();
            if (ImGui.Button("下一步##toAgreement"))
            {
                _readFirstPage = true;
#if !DEBUG
                _timeoutTask = Task.Run(async () =>
                {
                    for (int i = 60; i > 0; i--)
                    {
                        _timeoutLabel = $"{Strings.ToS.ButtonWillBeAvailableIn} {i}s";
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }
                });
#else
                _timeoutTask = Task.CompletedTask;
#endif
            }
        }
        else if (!_configService.Current.AcceptedAgreement && _readFirstPage)
        {
            Vector2 textSize;
            using (_uiShared.UidFont.Push())
            {
                textSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
                ImGui.TextUnformatted(Strings.ToS.AgreementLabel);
            }

            ImGui.SameLine();
            var languageSize = ImGui.CalcTextSize(Strings.ToS.LanguageLabel);
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - languageSize.X - 80);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2 - languageSize.Y / 2);

            ImGui.TextUnformatted(Strings.ToS.LanguageLabel);
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textSize.Y / 2 - (languageSize.Y + ImGui.GetStyle().FramePadding.Y) / 2);
            ImGui.SetNextItemWidth(80);
            if (ImGui.Combo("", ref _currentLanguage, _languages.Keys.ToArray(), _languages.Count))
            {
                GetToSLocalization(_currentLanguage);
            }

            ImGui.Separator();
            ImGui.SetWindowFontScale(1.5f);
            string readThis = Strings.ToS.ReadLabel;
            textSize = ImGui.CalcTextSize(readThis);
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2 - textSize.X / 2);
            UiSharedService.ColorText(readThis, ImGuiColors.DalamudRed);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Separator();

            UiSharedService.TextWrapped(_tosParagraphs![0]);
            UiSharedService.TextWrapped(_tosParagraphs![1]);
            UiSharedService.TextWrapped(_tosParagraphs![2]);
            UiSharedService.TextWrapped(_tosParagraphs![3]);
            UiSharedService.TextWrapped(_tosParagraphs![4]);
            UiSharedService.TextWrapped(_tosParagraphs![5]);

            ImGui.Separator();
            if (_timeoutTask?.IsCompleted ?? true)
            {
                if (ImGui.Button(Strings.ToS.AgreeLabel + "##toSetup"))
                {
                    _configService.Current.AcceptedAgreement = true;
                    _configService.Save();
                }
            }
            else
            {
                UiSharedService.TextWrapped(_timeoutLabel);
            }
        }
        else if (_configService.Current.AcceptedAgreement
                 && (string.IsNullOrEmpty(_configService.Current.CacheFolder)
                     || !_configService.Current.InitialScanComplete
                     || !Directory.Exists(_configService.Current.CacheFolder)))
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("文件存储设置");

            ImGui.Separator();

            if (!_uiShared.HasValidPenumbraModPath)
            {
                UiSharedService.ColorTextWrapped("未检测到有效的Penumbra模组路径，请在Penumbra里为模组目录设置一个有效的路径。", ImGuiColors.DalamudRed);
            }
            else
            {
                UiSharedService.TextWrapped("为了避免重复下载您已经拥有的模组文件，月海同步器需要扫描您的Penumbra模组目录。" +
                                     "另外，您还必须设置一个本地存储文件夹，月海同步器会将其他被同步角色的文件下载到其中。" +
                                     "设置好存储文件夹并扫描完成后，此页面将自动跳转到服务注册。");
                UiSharedService.TextWrapped("注意：初次扫描需要一些时间，时间长短取决于您拥有的模组数量。请耐心等待扫描完成。");
                UiSharedService.ColorTextWrapped("警告：一旦完成此步骤，您应该避免删除卫月目录下MareSynchronos配置文件夹中的FileCache.csv文件。" +
                                          "否则，在下次启动时，将重新扫描全部的模组文件来重建文件缓存数据库。", ImGuiColors.DalamudYellow);
                UiSharedService.ColorTextWrapped("警告：如果扫描卡住并且长时间没有进展，那么很可能是因为您的Penumbra文件路径设置不正确。", ImGuiColors.DalamudYellow);
                _uiShared.DrawCacheDirectorySetting();
            }

            if (!_cacheMonitor.IsScanRunning && !string.IsNullOrEmpty(_configService.Current.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_configService.Current.CacheFolder))
            {
                if (ImGui.Button("开始扫描##startScan"))
                {
                    _cacheMonitor.InvokeScan();
                }
            }
            else
            {
                _uiShared.DrawFileScanState();
            }
            if (!_dalamudUtilService.IsWine)
            {
                var useFileCompactor = _configService.Current.UseCompactor;
                if (ImGui.Checkbox("使用文件系统压缩", ref useFileCompactor))
                {
                    _configService.Current.UseCompactor = useFileCompactor;
                    _configService.Save();
                }
                UiSharedService.ColorTextWrapped("文件系统压缩可以大幅减少月海同步器下载的文件在硬盘上占用的空间。它会在下载时产生较小的CPU负荷，但可以加快其他角色的加载速度。" +
                    "建议保持启用状态。您也可以稍后在设置中随时更改此选项。", ImGuiColors.DalamudYellow);
            }
        }
        else if (!_uiShared.ApiController.ServerAlive)
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("服务注册");
            ImGui.Separator();
            UiSharedService.TextWrapped("您必须先注册一个账户，才能使用月海同步器。");
            UiSharedService.TextWrapped("为了安全，注册账户需要您去国服的月海同步器官方服务器Discord上进行处理，没有其他方式（也许您也可以问问亲爱的獭爹）。");
            UiSharedService.TextWrapped("如果您想在国服主服务器上进行注册 \"" + WebAPI.ApiController.MainServer + "\" 请加入Discord并在“月海命令”频道中按照指引通过服务机器人进行注册。");

            if (ImGui.Button("加入国服月海同步器Discord"))
            {
                Util.OpenLink("https://discord.gg/usCnZFhEHs");
            }

            UiSharedService.TextWrapped("如果您要加入非官方服务器，请联系该服务提供商来获取密钥。");

            UiSharedService.DistanceSeparator();

            UiSharedService.TextWrapped("一旦您获取到密钥，您就可以使用下面提供的工具来连接该服务。");

            int serverIdx = 0;
            var selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);

            using (var node = ImRaii.TreeNode("进阶选项"))
            {
                if (node)
                {
                    serverIdx = _uiShared.DrawServiceSelection(selectOnChange: true, showConnect: false);
                    if (serverIdx != _prevIdx)
                    {
                        _uiShared.ResetOAuthTasksState();
                        _prevIdx = serverIdx;
                    }

                    selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
                    _useLegacyLogin = !selectedServer.UseOAuth2;

                    if (ImGui.Checkbox("使用密钥登录（传统）", ref _useLegacyLogin))
                    {
                        _serverConfigurationManager.GetServerByIndex(serverIdx).UseOAuth2 = !_useLegacyLogin;
                        _serverConfigurationManager.Save();
                    }
                }
            }

            if (_useLegacyLogin)
            {
                var text = "输入密钥";
                var buttonText = "保存";
                var buttonWidth = _secretKey.Length != 64 ? 0 : ImGuiHelpers.GetButtonSize(buttonText).X + ImGui.GetStyle().ItemSpacing.X;
                var textSize = ImGui.CalcTextSize(text);

                ImGuiHelpers.ScaledDummy(5);
                UiSharedService.DrawGroupedCenteredColorText("请使用 OAuth2 方式验证, MareCN服务器目前仅支持OAuth2认证. " +
                    "OAuth2认证更为简单且无需管理复数密钥. " +
                    "既然已经使用Discord注册, 为什么不用更方便的OAuth2呢.", ImGuiColors.DalamudYellow, 500);
                ImGuiHelpers.ScaledDummy(5);

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(text);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - textSize.X);
                ImGui.InputText("", ref _secretKey, 64);
                if (_secretKey.Length > 0 && _secretKey.Length != 64)
                {
                    UiSharedService.ColorTextWrapped("您的密钥长度为64个字符，请不要在此输入石之家的验证码。", ImGuiColors.DalamudRed);
                }
                else if (_secretKey.Length == 64 && !HexRegex().IsMatch(_secretKey))
                {
                    UiSharedService.ColorTextWrapped("你的密钥应当只包括 ABCDEF 和 0-9.", ImGuiColors.DalamudRed);
                }
                else if (_secretKey.Length == 64)
                {
                    ImGui.SameLine();
                    if (ImGui.Button(buttonText))
                    {
                        if (_serverConfigurationManager.CurrentServer == null) _serverConfigurationManager.SelectServer(0);
                        if (!_serverConfigurationManager.CurrentServer!.SecretKeys.Any())
                        {
                            _serverConfigurationManager.CurrentServer!.SecretKeys.Add(_serverConfigurationManager.CurrentServer.SecretKeys.Select(k => k.Key).LastOrDefault() + 1, new SecretKey()
                            {
                                FriendlyName = $"设置时添加的密钥 ({DateTime.Now:yyyy-MM-dd})",
                                Key = _secretKey,
                            });
                            _serverConfigurationManager.AddCurrentCharacterToServer();
                        }
                        else
                        {
                            _serverConfigurationManager.CurrentServer!.SecretKeys[0] = new SecretKey()
                            {
                                FriendlyName = $"设置时添加的密钥 ({DateTime.Now:yyyy-MM-dd})",
                                Key = _secretKey,
                            };
                        }
                        _secretKey = string.Empty;
                        _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(selectedServer.OAuthToken))
                {
                    UiSharedService.TextWrapped("点击此按钮检查服务器是否允许OAuth2登录. 然后, 在打开的浏览器中用Discord登录.");
                    _uiShared.DrawOAuth(selectedServer);
                }
                else
                {
                    UiSharedService.ColorTextWrapped($"OAuth2 已启用, 连接到: Discord 用户 {_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)}", ImGuiColors.HealerGreen);
                    UiSharedService.TextWrapped("请点击 更新UID 按钮来获取你在本服务器上的所有UID.");
                    _uiShared.DrawUpdateOAuthUIDsButton(selectedServer);
                    var playerName = _dalamudUtilService.GetPlayerName();
                    var playerWorld = _dalamudUtilService.GetHomeWorldId();
                    UiSharedService.TextWrapped($"之后, 请选择你想分配给当前角色 {_dalamudUtilService.GetPlayerName()} 的UID. 如果没有可用的UID, 请确认你登录了正确的Discord账号. " +
                        $"如果账号错误, 请点击下方的取消连接按钮 (按住CTRL).");
                    _uiShared.DrawUnlinkOAuthButton(selectedServer);

                    var auth = selectedServer.Authentications.Find(a => string.Equals(a.CharacterName, playerName, StringComparison.Ordinal) && a.WorldId == playerWorld);
                    if (auth == null)
                    {
                        auth = new Authentication()
                        {
                            CharacterName = playerName,
                            WorldId = playerWorld
                        };
                        selectedServer.Authentications.Add(auth);
                        _serverConfigurationManager.Save();
                    }

                    _uiShared.DrawUIDComboForAuthentication(0, auth, selectedServer.ServerUri);

                    using (ImRaii.Disabled(string.IsNullOrEmpty(auth.UID)))
                    {
                        if (_uiShared.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Link, "连接到服务器"))
                        {
                            _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                        }
                    }
                    if (string.IsNullOrEmpty(auth.UID))
                        UiSharedService.AttachToolTip("选择一个UID以连接到服务器");
                }
            }
        }
        else
        {
            Mediator.Publish(new SwitchToMainUiMessage());
            IsOpen = false;
        }
    }

    private void GetToSLocalization(int changeLanguageTo = -1)
    {
        if (changeLanguageTo != -1)
        {
            _uiShared.LoadLocalization(_languages.ElementAt(changeLanguageTo).Value);
        }

        _tosParagraphs = [Strings.ToS.Paragraph1, Strings.ToS.Paragraph2, Strings.ToS.Paragraph3, Strings.ToS.Paragraph4, Strings.ToS.Paragraph5, Strings.ToS.Paragraph6];
    }

    [GeneratedRegex("^([A-F0-9]{2})+")]
    private static partial Regex HexRegex();
}