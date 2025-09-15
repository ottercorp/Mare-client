using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using MareSynchronos.Services.ServerConfiguration;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public class CensusPopupHandler : IPopupHandler
{
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;

    public CensusPopupHandler(ServerConfigurationManager serverConfigurationManager, UiSharedService uiSharedService)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _uiSharedService = uiSharedService;
    }

    private Vector2 _size = new(600, 450);
    public Vector2 PopupSize => _size;

    public bool ShowClose => false;

    public void DrawContent()
    {
        var start = 0f;
        using (_uiSharedService.UidFont.Push())
        {
            start = ImGui.GetCursorPosY() - ImGui.CalcTextSize("Mare角色数据普查").Y;
            UiSharedService.TextWrapped("参与Mare角色数据普查");
        }
        ImGuiHelpers.ScaledDummy(5f);
        UiSharedService.TextWrapped("请仔细阅读以下内容.");
        ImGui.Separator();
        UiSharedService.TextWrapped("Mare角色数据普查是一个仅为了数据统计而进行的数据收集活动. " +
            "所有收集的数据仅会与你的UID关联,被短暂的存储在服务器上,这些数据将于你与服务器断开连接时删除. " +
            "这些数据不会被用户长期追踪特定用户.");
        UiSharedService.TextWrapped("如果开启,你将发送以下数据:" + Environment.NewLine
            + "- 角色所在服务器" + Environment.NewLine
            + "- 角色性别 (Glamourer生效后数据)" + Environment.NewLine
            + "- 角色种族 (Glamourer生效后数据)" + Environment.NewLine
            + "- 角色氏族 (逐日之民等., Glamourer生效后数据)");
        UiSharedService.TextWrapped("如果同意发送以上数据,请点击下方按钮.");
        UiSharedService.TextWrapped("本设置可以随时开启或关闭.");
        var width = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var buttonSize = ImGuiHelpers.GetButtonSize("我同意发送我的角色数据");
        ImGuiHelpers.ScaledDummy(5f);
        if (ImGui.Button("我同意发送我的角色数据", new Vector2(width, buttonSize.Y * 2.5f)))
        {
            _serverConfigurationManager.SendCensusData = true;
            _serverConfigurationManager.ShownCensusPopup = true;
            ImGui.CloseCurrentPopup();
        }
        ImGuiHelpers.ScaledDummy(1f);
        if (ImGui.Button("我不同意发送角色数据", new Vector2(width, buttonSize.Y)))
        {
            _serverConfigurationManager.SendCensusData = false;
            _serverConfigurationManager.ShownCensusPopup = true;
            ImGui.CloseCurrentPopup();
        }
        var height = ImGui.GetCursorPosY() - start;
        _size = _size with { Y = height };
    }
}
