using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI
{
    public class ChangelogUi : WindowMediatorSubscriberBase
    {

        private const string Version = "25-08-15";

        private readonly ILogger<ChangelogUi> _logger;
        private UiSharedService _uiSharedService;
        private MareConfigService _mareConfig;
        private readonly DalamudUtilService _dalamudUtilService;

        private int count = 0;


        public ChangelogUi(ILogger<ChangelogUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
            UiSharedService uiSharedService, MareConfigService mareConfig, DalamudUtilService dalamudUtilService
            ) : base(logger, mediator, "功能展示", performanceCollectorService)
        {
            _uiSharedService = uiSharedService;
            _mareConfig = mareConfig;
            _dalamudUtilService = dalamudUtilService;
            _logger = logger;

            IsOpen = !string.Equals(_mareConfig.Current.ChangeLogVersion, CalculateHash, StringComparison.OrdinalIgnoreCase);

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(800, 600),
                MaximumSize = new Vector2(1000, 2000),
            };

            Flags |= ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize;

        }

        private string CalculateHash => (DalamudUtilService.GetDeviceId() + Version).GetHash256();
        private bool IsRead => (count ^ 0b111) == 0;
        private float ButtonSize => _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.WindowClose, "关闭");


        private void DrawNew()
        {
            UiSharedService.ColorTextWrapped("[ NEW! ]", ImGuiColors.DalamudOrange);
            ImGui.SameLine();
        }

        protected override void DrawInternal()
        {

            _uiSharedService.BigText("新功能介绍");
            ImGui.Separator();
            if (ImGui.BeginChild("###Content", new Vector2(0, -50)))
            {
                _uiSharedService.BigText("月海招募");
                if (ImGui.TreeNodeEx("如何打开###进入招募"))
                {
                    UiSharedService.TextWrapped("1. 使用命令 ‘/mare pf’ 打开招募中心");
                    UiSharedService.TextWrapped($"2. 点击Mare主界面顶端 ");

                    UiSharedService.TextWrapped($" 按钮 打开招募中心");
                    UiSharedService.TextWrapped("3. 点击聊天框中定期提示的招募状态信息中的 [ 打开月海招募中心 ] 链接");
                    DrawReadButton(0);
                    ImGui.TreePop();
                }

                DrawNew();
                UiSharedService.TextWrapped("招募提示消息现在会跟随游戏原生招募提示同步出现(除登录首次)，请自行修改游戏提示间隔.");

                ImGui.Separator();

                _uiSharedService.BigText("同步贝聊天");
                if (ImGui.TreeNodeEx("如何使用###进入聊天"))
                {
                    UiSharedService.TextWrapped("1. 使用命令 ‘/mare chat’ - 打开聊天框\n  '/mare r' - 回复上一个同步贝聊天\n  '/mare 同步贝名' - 回复特定同步贝聊天");
                    UiSharedService.TextWrapped($"2. 点击Mare主界面顶端 ");
                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.Blog);
                    ImGui.SameLine();
                    UiSharedService.TextWrapped($"按钮 打开聊天框");
                    UiSharedService.TextWrapped($"3. 点击对应群组右侧 ");
                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.EllipsisV);
                    ImGui.SameLine();
                    UiSharedService.TextWrapped($" 按钮, 加入对应群组的聊天");
                    UiSharedService.ColorTextWrapped("注意：贝的拥有者需要在 '同步贝设置' 中打开贝聊天功能才能使用", ImGuiColors.DalamudYellow);
                    DrawReadButton(1);
                    ImGui.TreePop();
                }
                if (ImGui.TreeNodeEx("修改配置###聊天设置"))
                {
                    UiSharedService.TextWrapped("1. 在设置界面 - UI - 国服特供部分修改相关设置");
                    UiSharedService.ColorTextWrapped("提供了Chat2集成, 默认开启", ImGuiColors.ParsedBlue);
                    DrawReadButton(2);
                    ImGui.TreePop();
                }

                ImGui.Separator();
            }

            if (!IsRead)
            {
                UiSharedService.DrawGroupedCenteredColorText("你必须确认以上所有内容才能点击下方按钮", ImGuiColors.DalamudRed);
            }


            ImGui.BeginDisabled(!IsRead);
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - ButtonSize) / 2);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.WindowClose, "关闭"))
            {
                _mareConfig.Current.ChangeLogVersion = CalculateHash;
                _mareConfig.Save();
                IsOpen = false;
            }
            ImGui.EndDisabled();
        }

        private void DrawReadButton(int i)
        {
            ImGui.BeginDisabled(((count >> i) & 0b1) == 1 );
            if (ImGui.Button($"我已了解###{i}"))
            {
                var num = 1 << i;
                count |= num;
            }
            ImGui.EndDisabled();
        }

    }
}