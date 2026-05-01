using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI
{
    public partial class PFinderWindow : WindowMediatorSubscriberBase
    {

        private readonly MareConfigService _configService;
        private readonly UiSharedService _uiShared;
        private readonly ApiController _apiController;
        private readonly IChatGui _chatGui;
        private readonly DalamudLinkPayload _pfinderChatLinkPayload;
        private readonly ushort[] colors = new ushort[] { 1, 17, 25, 37, 43, 48, 524 };

        private readonly TimeSpan CoolDown = TimeSpan.FromSeconds(15);

        public static List<PFinderDto> Pfs = [];
        private DateTime _lastUpdate = DateTime.MinValue;
        CancellationTokenSource cts = new();
        private string _fliter = "";
        private bool Disable => _lastUpdate + CoolDown > DateTime.Now;
        private Task? _pfinderUpdateTask;

        private readonly Regex _gamePfString = new(@"当前共有\d*个队伍正在招募队员");

        public PFinderWindow(ILogger<PFinderWindow> logger, MareConfigService configService, MareMediator mareMediator,
            PerformanceCollectorService performanceCollectorService, ApiController apiController, IChatGui chatGui,
            UiSharedService uiShared) : base(logger, mareMediator, "招募中心", performanceCollectorService)
        {
            _configService = configService;
            _apiController = apiController;
            _chatGui = chatGui;
            _uiShared = uiShared;
            _pfinderChatLinkPayload = _chatGui.AddChatLinkHandler(369852, OnPfinderLinkClicked);
            IsOpen = false;
            ShowCloseButton = true;
            RespectCloseHotkey = false;
            AllowClickthrough = false;
            AllowPinning = false;

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(800, 400),
                MaximumSize = new Vector2(800, 2000),
            };

            Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
            {
                cts?.Cancel();
            });
            Mediator.Subscribe<OpenPfinderWindowMessage>(this, (msg) =>
            {
                IsOpen = true;
                _fliter = msg.Fliter;
            });
            Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
            {
                if (_pfinderUpdateTask == null || _pfinderUpdateTask.IsCompleted)
                {
                    if (cts.IsCancellationRequested)
                    {
                        cts.Dispose();
                        cts = new CancellationTokenSource();
                    }

                    _pfinderUpdateTask = UpdatePFs(cts.Token);
                }
            } );

            chatGui.ChatMessage += ChatGuiOnChatMessage;
        }

        private void ChatGuiOnChatMessage(IHandleableChatMessage message)
        {
            if ((uint)message.LogKind != 72) return;
            if (!_gamePfString.IsMatch(message.Message.TextValue)) return;
            if (!_configService.Current.ShowPFinder) return;

            PrintPFCount();
        }

        private void PrintPFCount()
        {
            var prefix = $"\uE044月海招募中心有{Pfs.Count}条招募信息";
            if (!string.IsNullOrEmpty(_fliter))
            {
                var count = Pfs.Count(pf => string.Join("|", pf.Title, pf.Description, pf.Tags, pf.Group.AliasOrGID, pf.User.AliasOrUID).Contains(_fliter));
                if (count > 0)
                {
                    prefix += $", 其中有{count}条符合当前筛选";
                }
            }
            prefix += " . ";

            var msg = new SeString(
                new TextPayload(prefix),
                _pfinderChatLinkPayload,
                new UIForegroundPayload(colors[_configService.Current.ChatColor]),
                new TextPayload("[ 打开月海招募中心 ]"),
                new UIForegroundPayload(0),
                RawPayload.LinkTerminator
            );

            _chatGui.Print(new XivChatEntry { Message = msg, Type = XivChatType.SystemMessage });
        }

        private async Task UpdatePFs(CancellationToken ct)
        {
            if (!_configService.Current.ShowPFinder) return;
            var first = true;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    if (!_apiController.IsConnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                        continue;
                    }

                    Pfs = await _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).ConfigureAwait(false);
                    if (first)
                    {
                        first = false;
                        PrintPFCount();
                    }

                    await Task.Delay(TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send PFinder notice: ");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                }
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
                _logger.LogDebug(ex, "Failed to open PFinder from chat link");
            }
        }

        public override void OnOpen()
        {
            if (!_apiController.IsConnected) return;
            if (_lastUpdate + CoolDown < DateTime.Now)
            {
                Pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
                if (Pfs.Count > 0) _lastUpdate = DateTime.Now;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
            _chatGui.ChatMessage -= ChatGuiOnChatMessage;
            _chatGui.RemoveChatLinkHandler(369852);
            base.Dispose(disposing);
        }

        protected override void DrawInternal()
        {
            if (!_apiController.IsConnected) return;

            ImGui.SetNextItemWidth(750);
            ImGui.InputText("筛选##Fliter", ref _fliter, 64);

            var bottomBarHeight = ImGui.GetFrameHeightWithSpacing() + 5.0f;
            var childsize = new Vector2(0, -bottomBarHeight);
            if (ImGui.BeginChild("##PFlist", childsize, true, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                foreach (var pf in Pfs)
                {
                    var str = string.Join("|", pf.Title, pf.Description, pf.Tags, pf.Group.AliasOrGID, pf.User.AliasOrUID);
                    if (!string.IsNullOrEmpty(_fliter) && !str.Contains(_fliter)) continue;
                    DrawPF(pf);
                }
                ImGui.EndChild();
            }

            ImGui.BeginDisabled(Disable);
            if (ImGui.Button("刷新"))
            {
                _lastUpdate = DateTime.Now;
                Pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (Disable)
            {
                ImGui.SameLine();
                var time = _lastUpdate + CoolDown - DateTime.Now;
                ImGui.Text($"冷却中 : {time:mm\\:ss}");
            }

            ImGui.SameLine(380);
            if (ImGui.Button("创建"))
            {
                var alias = _apiController.DisplayName == _apiController.UID ? null : _apiController.DisplayName;
                Mediator.Publish(new OpenPFinderPopupMessage(new PFinderDto(){User = new UserData(_apiController.UID, alias)}));
            }

        }

        private void DrawPF(PFinderDto pf)
        {
            // 使用一个带边框的表格来包裹整个条目。
            if (ImGui.BeginTable("pf_card_" + pf.Guid, 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
            {
                // === 定义列的属性 ===
                ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 30f);

                // === 绘制表格内容 ===
                ImGui.TableNextRow();

                // --- 第一列：内容区 ---
                ImGui.TableSetColumnIndex(0);

                _uiShared.BigText(pf.Title, ImGuiColors.ParsedBlue);

                if (pf.IsNSFW)
                {
                    UiSharedService.ColorText("NSFW", ImGuiColors.DalamudRed);
                    UiSharedService.AttachToolTip("NSFW/R18+");
                    ImGui.SameLine();
                }
                UiSharedService.ColorText(pf.Tags, ImGuiColors.DalamudGrey);

                var goingon = pf.StartTime < DateTime.Now && pf.EndTime > DateTime.Now;
                var passed = pf.EndTime < DateTime.Now;
                if (goingon)
                {
                    UiSharedService.ColorTextWrapped($"{pf.StartTime.ToLocalTime():g} - {pf.EndTime.ToLocalTime():g}", ImGuiColors.ParsedGreen);
                }
                else if (passed)
                {
                    UiSharedService.ColorTextWrapped($"{pf.StartTime.ToLocalTime():g} - {pf.EndTime.ToLocalTime():g}", ImGuiColors.DalamudRed);
                }
                else
                {
                    UiSharedService.ColorTextWrapped($"{pf.StartTime.ToLocalTime():g} - {pf.EndTime.ToLocalTime():g}", ImGuiColors.DalamudWhite);
                }


                // 将组信息和用户信息并排显示
                UiSharedService.TextWrapped(pf.Open ? "公开" : $"{pf.Group.AliasOrGID}");
                ImGui.SameLine(ImGui.GetColumnWidth() - 200); // 使用相对定位，更健壮
                UiSharedService.ColorTextWrapped(pf.User.AliasOrUID, UiSharedService.IsSupporter(pf.User.UID) ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite);

                // --- 修改开始 ---

                // 我们仍然使用 Child 窗口来创建一个固定高度、带滚动条的区域
                ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5.0f);
                if (ImGui.BeginChild("desc_child_" + pf.Guid, new Vector2(0, 105), true))
                {
                    // 1. 创建一个临时的 string 变量，因为 InputTextMultiline 需要一个 `ref string`
                    var descriptionText = pf.Description ?? string.Empty;

                    // 2. (推荐) 移除输入框的背景和边框，让它看起来像普通文本
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0)); // 透明背景
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0)); // 移除内边距

                    // 3. 使用 InputTextMultiline 并设置 ReadOnly 标志
                    //    - 使用唯一的隐藏标签 "##..."
                    //    - 尺寸设置为 new Vector2(-1, -1) 或 GetContentRegionAvail() 以填满 Child 容器
                    //    - 传入 ImGuiInputTextFlags.ReadOnly
                    ImGui.InputTextMultiline("##desc_text" + pf.Guid,
                        ref descriptionText,
                        descriptionText.Length + 1, // MaxLength，在只读模式下不重要
                        ImGui.GetContentRegionAvail(),
                        ImGuiInputTextFlags.ReadOnly);

                    // 4. 恢复样式
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor();
                }
                ImGui.EndChild();
                ImGui.PopStyleVar();

                // --- 修改结束 ---

                // --- 第二列：操作区 ---
                ImGui.TableSetColumnIndex(1);

                if (pf.User.UID == _apiController.UID)
                {
                    if (ImGui.Button("修改##" + pf.Guid))
                    {
                        Mediator.Publish(new OpenPFinderPopupMessage(pf.DeepClone()));
                    }

                    ImGui.BeginDisabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl));
                    if (ImGui.Button("删除##" + pf.Guid))
                    {
                        var clone = pf.DeepClone();
                        clone.StartTime = DateTimeOffset.MinValue;
                        clone.EndTime = DateTimeOffset.MinValue.AddMinutes(1);
                        var result = _apiController.UpdatePFinder(clone).Result;
                        Pfs = _apiController.RefreshPFinderList(new UserDto(new UserData(_apiController.UID))).Result;
                    }
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    {
                        UiSharedService.AttachToolTip("按住Ctrl键以删除");
                    }
                }
                if (pf.HasTempGroup && !string.IsNullOrEmpty(pf.TempGroupPW))
                {
                    ImGui.BeginDisabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl));
                    if (ImGui.Button("加入"))
                    {
                        _ = _apiController.GroupJoinFinalize(new GroupJoinDto(pf.Group, pf.TempGroupPW,
                            GroupUserPreferredPermissions.NoneSet));
                    }
                    UiSharedService.AttachToolTip($"按住Ctrl并点击将加入临时同步贝 {pf.Group.AliasOrGID}");
                    ImGui.EndDisabled();
                }

                // === 结束表格 ===
                ImGui.EndTable();
            }
        }
    }
}