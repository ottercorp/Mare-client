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

        private const string Version = "25-09-30";

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

            IsOpen = !string.Equals(_mareConfig.Current.ChangeLogVersion, CalculateHash, StringComparison.OrdinalIgnoreCase);

            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(800, 600),
                MaximumSize = new Vector2(1000, 2000),
            };

            Flags |= ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize;

        }

        private string CalculateHash => (DalamudUtilService.GetDeviceId() + Version).GetHash256();
        private bool IsRead => (count ^ 0b1) == 0;
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

                DrawNew();
                _uiSharedService.BigText("位置共享");
                if (ImGui.TreeNodeEx("如何打开###howtoopen1"))
                {
                    UiSharedService.ColorTextWrapped("该功能默认关闭, 需要手动对单个用户开启/关闭, 或通过同步贝菜单批量操作开关.", ImGuiColors.DalamudYellow);
                    ImGui.Spacing();
                    UiSharedService.TextWrapped("你可以从主界面任意已配对玩家的选项菜单（三个点形状的按钮）内选择打开或关闭与其共享自己的位置.");
                    UiSharedService.TextWrapped("你也可以通同步贝的选项菜单（同上） 来快速打开/关闭对该贝内所有用户的位置共享.");
                    UiSharedService.TextWrapped("请注意通过同步贝进行操作无法覆盖你手动设置独立配置的配对角色.");
                    ImGui.Spacing();
                    UiSharedService.TextWrapped("打开共享后会在主界面显示对应图标, 鼠标悬浮即可查看对方位置以及自己的位置共享情况");
                    ImGui.Spacing();
                    DrawReadButton(0);
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