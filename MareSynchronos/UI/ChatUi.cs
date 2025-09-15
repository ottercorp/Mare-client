using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI
{
    public class ChatUi : WindowMediatorSubscriberBase
    {
        public static List<string> JoinedGroups = new();

        private readonly ILogger<ChatUi> _logger;
        private UiSharedService _uiSharedService;
        private ApiController _apiController;
        private PairManager _pairManager;
        private IdDisplayHandler _idDisplayHandler;
        private MareConfigService _mareConfig;
        private NotificationService _notificationService;

        private string _newMessage = string.Empty;
        private static List<ChatMessage> _chatLogs = new();
        private string _lastActiveGroup;
        public static string LastChatGroup = string.Empty;

        private const string Global = "MSS-GLOBAL";

        public ChatUi(ILogger<ChatUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
            UiSharedService uiSharedService, ApiController apiController, PairManager pairManager,IdDisplayHandler idDisplayHandler,
            MareConfigService mareConfig, NotificationService notificationService) : base(logger, mediator, "同步贝聊天", performanceCollectorService)
        {
            _uiSharedService = uiSharedService;
            _apiController = apiController;
            _pairManager = pairManager;
            _idDisplayHandler = idDisplayHandler;
            _mareConfig = mareConfig;
            _notificationService = notificationService;
            _logger = logger;

            Mediator.Subscribe<ChatMessage>(this, HandleChatMessage);
            mediator.Subscribe<OpenChatUi>(this, _ => IsOpen = true);
            IsOpen = _mareConfig.Current.ShowChatWindowOnLogin;

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(375, 400),
                MaximumSize = new Vector2(1000, 2000),
            };
            JoinedGroups = new List<string>(_mareConfig.Current.AutoJoinChats);
        }

        private void HandleChatMessage(ChatMessage msg)
        {
            if (!JoinedGroups.Contains(msg.Group.GID)) return;
            _chatLogs.Add(msg);
            if (_chatLogs.Count(x => x.Group.GID == msg.Group.GID) > 50)
            {
                _chatLogs.RemoveAt(_chatLogs.FindIndex(x => x.Group.GID == msg.Group.GID));
            }
            _logger.LogDebug($"Received chat message: '{msg.Message}' from {msg.Sender.AliasOrUID} in group {msg.Group.AliasOrGID}");
            // 若 ChatTwo 已连接，则不再将聊天输出到默认聊天框，避免重复
            if (_mareConfig.Current.PortToChatGui && !_uiSharedService.ChatTwoExists)
            {
                var groupName = GetGroupName(msg.Group.GID);
                Mediator.Publish(new NotificationMessage(groupName, $"<{GetName(msg)}> : " + msg.Message, NotificationType.Chat));
            }
        }

        protected override void DrawInternal()
        {
            if (!_apiController.IsConnected) return;
            using (_uiSharedService.GameFont.Push())
            {
                if (JoinedGroups.Count == 0)
                {
                    ImGui.Text("你还没有加入任何同步贝聊天, 请先加入一个再尝试聊天.");
                }
                else if (ImGui.BeginTabBar("ChatLogs"))
                {

                    var groups = new List<string>(JoinedGroups);
                    foreach (string group in groups)
                    {
                        if (_pairManager.Groups.All(x => x.Key.GID != group)
                            && !string.Equals(group, Global, StringComparison.OrdinalIgnoreCase)) continue;
                        var IsOpen = true;

                        var groupName = GetGroupName(group);
                        if (ImGui.BeginTabItem(groupName, ref IsOpen))
                        {
                            if (_lastActiveGroup != group)
                            {
                                _newMessage = string.Empty;
                                _lastActiveGroup = group;
                            }

                            DrawChatLog(group);
                            ImGui.EndTabItem();
                        }

                        if (!IsOpen)
                        {
                            JoinedGroups.Remove(group);
                            Mediator.Publish(new JoinedGroupsChangedMessage());
                            if (_lastActiveGroup == group)
                            {
                                _lastActiveGroup = null;
                                _newMessage = string.Empty;
                            }
                        }
                    }
                    ImGui.EndTabBar();
                }

            }
        }



        private void DrawChatLog(string group)
        {
            unsafe
            {
                // 计算输入框的动态高度
                float availableWidth = ImGui.GetContentRegionAvail().X - 50; // 窗口可用宽度减去按钮宽度
                float inputHeight = ImGui.GetFrameHeightWithSpacing();

                // 设置聊天记录区域的高度，确保留出输入区域的空间
                float totalInputAreaHeight = inputHeight * 2 + ImGui.GetStyle().ItemSpacing.Y * 2; // 输入框 + 分隔线 + 按钮
                ImGui.BeginChild($"{group}##chatlog", new Vector2(0, -totalInputAreaHeight), true);
                foreach (ChatMessage msg in _chatLogs.Where(x => x.Group.GID == group))
                {
                    if (msg.Sender.UID == "SYSTEM-INFO")
                    {
                        ImGui.TextWrapped($"[{msg.LocalTime:HH:mm:ss}] 系统信息: {msg.Message}");
                        continue;
                    }

                    var name = GetName(msg);
                    var color = UiSharedService.IsSupporter(msg.Sender.UID) ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite2;
                    ImGui.TextUnformatted($"[{msg.LocalTime:HH:mm:ss}]");
                    ImGui.SameLine();
                    UiSharedService.ColorText($"{name}", color);
                    ImGui.TextWrapped($"{msg.Message}");
                    ImGui.Spacing();
                }
                // 自动滚动到最新消息
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                    ImGui.SetScrollHereY(1.0f);
                ImGui.EndChild();

                // 分隔线
                ImGui.Separator();

                var autoJoin = _mareConfig.Current.AutoJoinChats.Contains(group);
                if (ImGui.Checkbox("自动加入该聊天", ref autoJoin))
                {
                    if (autoJoin)
                    {
                        if (!_mareConfig.Current.AutoJoinChats.Contains(group))
                            _mareConfig.Current.AutoJoinChats.Add(group);
                    }
                    else if (_mareConfig.Current.AutoJoinChats.Contains(group))
                        _mareConfig.Current.AutoJoinChats.Remove(group);
                    _mareConfig.Save();
                }

                // 使用精确的宽度确保换行一致
                var send = ImGui.InputTextMultiline("##chat_input", ref _newMessage, 4096,
                    new Vector2(availableWidth, inputHeight),
                    ImGuiInputTextFlags.CtrlEnterForNewLine | ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (inputHeight - ImGui.GetFrameHeight()) / 2); // 按钮底部对齐输入框
                if (ImGui.Button("发送") || send)
                {
                    if (!string.IsNullOrEmpty(_newMessage))
                    {
                        var msg = new GroupChatDto(new UserData(_apiController.UID, _apiController.DisplayName), new GroupData(group), DateTime.UtcNow, _newMessage);
                        _ = _apiController.GroupChatServer(msg);
                        _newMessage = string.Empty; // 清空输入框
                    }
                }
            }
        }

        private string GetName(ChatMessage msg)
        {
            if (_apiController.UID == msg.Sender.UID)
                return _apiController.DisplayName;

            var pair = _pairManager.GetPairByUID(msg.Sender.UID);
            if (pair is null)
                return msg.Sender.AliasOrUID;
            return _idDisplayHandler.GetPlayerText(pair).text;
        }

        private string GetGroupName(string group)
        {
            if (group == "MSS-GLOBAL") return "世界";
            var pairedgroup = _pairManager.Groups.Select(x => x.Value).FirstOrDefault(x => x.GID == group);
            if (pairedgroup is null) return group;
            // return _idDisplayHandler
            //     .GetGroupText(pairedgroup).text;
            return pairedgroup.GroupAliasOrGID;
        }

    }
}