using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public class PFinderPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private PFinderDto pf;
    private string pfTitle;
    string pfDescription;
    bool pfIsNsfw;
    string pfTags;
    DateTimeOffset pfStartTime;
    DateTimeOffset pfEndTime;
    bool hasTempGroup;
    int index;
    GroupFullInfoDto[] groups = [];
    private GroupJoinDto? tempGroup = null;
    private readonly bool IsSupporter = false;

    private int DaysLimitForCreate = 3;
    private int DaysLimitForLength = 1;



    public PFinderPopupHandler(ApiController apiController, UiSharedService uiSharedService, PairManager pairManager)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        IsSupporter = UiSharedService.IsSupporter(_apiController.UID);
        if (IsSupporter)
        {
            DaysLimitForCreate = 7;
            DaysLimitForLength = 3;
        }
    }

    public Vector2 PopupSize => new(800, 600);

    public bool ShowClose => false;

    public void DrawContent()
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

            _uiSharedService.BigText(pf.Title, ImGuiColors.ParsedBlue);

            if (pf.IsNSFW)
            {
                UiSharedService.ColorText("NSFW", ImGuiColors.DalamudRed);
                UiSharedService.AttachToolTip("NSFW/R18+");
                ImGui.SameLine();
            }
            UiSharedService.ColorText(pf.Tags, ImGuiColors.DalamudGrey);

            var goingon = pf.StartTime < DateTime.Now && pf.EndTime > DateTime.Now;
            UiSharedService.ColorTextWrapped($"{pf.StartTime.ToLocalTime():g} - {pf.EndTime.ToLocalTime():g}", goingon ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudWhite);

            // 将组信息和用户信息并排显示
            UiSharedService.TextWrapped(pf.Open ? "公开" : $"{pf.Group.AliasOrGID}");
            ImGui.SameLine(ImGui.GetColumnWidth() - 200); // 使用相对定位，更健壮
            UiSharedService.ColorTextWrapped(pf.User.AliasOrUID, UiSharedService.IsSupporter(pf.User.UID) ? ImGuiColors.ParsedGold : ImGuiColors.DalamudWhite);

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

            // === 结束表格 ===
            ImGui.EndTable();
        }

        UiSharedService.DrawGroupedCenteredColorText("↑ 预览 ↑", ImGuiColors.ParsedGreen);

        if (ImGui.BeginChild(pf.Guid.ToString(), new Vector2(780,300), true))
        {

            ImGui.Text("标题:");
            ImGui.SameLine();
            if (ImGui.InputText("##标题", ref pfTitle, 120))
            {
                pf.Title = pfTitle;
            }
            if (string.IsNullOrEmpty(pf.Title))
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped("必填", ImGuiColors.DPSRed);
            }


            if (ImGui.Checkbox("##NSFW", ref pfIsNsfw))
            {
                pf.IsNSFW = pfIsNsfw;
            }
            ImGui.SameLine();
            UiSharedService.ColorText("NSFW", ImGuiColors.DalamudRed);
            UiSharedService.AttachToolTip("NSFW/R18+");


            ImGui.Text("Tag:");
            ImGui.SameLine();
            if (ImGui.InputText("##Tag", ref pfTags, 200))
            {
                pf.Tags = pfTags;
            }


            ImGui.Text("起始时间:");
            ImGui.SameLine();
            if (ImGuiAdvancedWidgets.DateTimePickerInLocalZone("my_dt_picker", ref pfStartTime))
            {
                pf.StartTime = pfStartTime;
            }

            ImGui.SameLine();
            ImGui.Text(" -  结束时间:");
            ImGui.SameLine();

            if (ImGuiAdvancedWidgets.DateTimePickerInLocalZone("my_dt_picker2", ref pfEndTime))
            {
                pf.EndTime = pfEndTime;
            }

            if (pf.StartTime > DateTime.Now + TimeSpan.FromDays(DaysLimitForCreate))
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped($"开始时间不能超过{DaysLimitForCreate}天.", ImGuiColors.DPSRed);
            }

            if (pf.StartTime > pf.EndTime)
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped("开始时间不能晚于结束时间.", ImGuiColors.DPSRed);
            }
            if (pf.EndTime < DateTime.Now)
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped("结束时间不能早于当前时间.", ImGuiColors.DPSRed);
            }
            else if (pf.EndTime < DateTime.Now.AddHours(1))
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped("将在一小时内结束.", ImGuiColors.DalamudYellow);
            }

            if (pf.StartTime + TimeSpan.FromDays(DaysLimitForLength) < pf.EndTime)
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped($"持续时间不能超过{DaysLimitForLength}天.", ImGuiColors.DPSRed);
            }

            ImGui.Text("描述:");
            ImGui.SameLine();
            if (ImGui.InputTextMultiline("##描述", ref pfDescription, 1000,
                    new Vector2(600, ImGui.GetTextLineHeight() * 4), ImGuiInputTextFlags.NoHorizontalScroll))
            {
                pf.Description = pfDescription;
            }
            if (string.IsNullOrEmpty(pf.Description))
            {
                ImGui.SameLine();
                UiSharedService.ColorTextWrapped("必填", ImGuiColors.DPSRed);
            }

            bool pfOpen = pf.Open;
            ImGui.BeginDisabled(!string.IsNullOrEmpty(pf.TempGroupPW));
            if (ImGui.Checkbox("公开", ref pfOpen))
            {
                pf.Open = pfOpen;
                if (pf.Open) pf.Group = new GroupData("MSS-GLOBAL", "MareCN公用贝");
                else
                {
                    pf.Group = new GroupData(groups[index].GID, groups[index].GroupAlias);
                }
            }
            UiSharedService.AttachToolTip("选中后所有用户都能看到此招募,否则仅有下方贝中用户可以查看.");
            ImGui.EndDisabled();

            if (!pf.Open)
            {
                if (groups.Length > 0)
                {
                    ImGui.Text("所在贝:");
                    ImGui.SameLine();
                    if (ImGui.Combo("##所在贝", ref index, groups.Select(x=> x.GroupAliasOrGID).ToArray(), groups.Length))
                    {
                        pf.Group = new GroupData(groups[index].GID, groups[index].GroupAlias);
                    }
                }
                UiSharedService.ColorText("*你必须有至少一个贝的管理权限才能发布非公开招募.", ImGuiColors.DalamudYellow);
            }
            else
            {
                ImGui.BeginDisabled(!string.IsNullOrEmpty(pf.TempGroupPW));
                if (ImGui.Checkbox("使用临时同步贝", ref hasTempGroup))
                {
                    pf.HasTempGroup = hasTempGroup;
                }
                ImGui.EndDisabled();
                if (pf.HasTempGroup && string.IsNullOrEmpty(pf.TempGroupPW))
                {
                    if (ImGui.Button("创建一个临时同步贝"))
                    {
                        var result = _apiController.GroupCreate().Result;
                        pf.Group = new GroupData(result.GID, result.GroupAlias);
                        pf.TempGroupPW = result.Password;
                    }
                }

                if (pf.HasTempGroup && !string.IsNullOrEmpty(pf.TempGroupPW))
                {
                    ImGui.Text($"已创建临时同步贝:");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("同步贝ID: " + pf.Group.GID);
                    ImGui.SameLine();
                    ImGui.TextUnformatted("同步贝密码: " + pf.TempGroupPW);
                    UiSharedService.ColorText("请勿修改同步贝密码或删除贝,否则参与者将无法加入.", ImGuiColors.DalamudYellow);
                    ImGui.SameLine();
                    ImGui.TextUnformatted("如需同步贝聊天请自行前往贝管理界面开启.");


                    ImGui.BeginDisabled(!ImGui.IsKeyDown(ImGuiKey.ModCtrl));
                    if (ImGui.Button("删除临时贝"))
                    {
                        _ = _apiController.GroupDelete(new GroupDto(pf.Group));
                        pf.Group = new GroupData("MSS-GLOBAL", "MareCN公用贝");
                        pf.TempGroupPW = null;
                    }
                    UiSharedService.AttachToolTip("按住Ctrl并点击以删除");
                    ImGui.EndDisabled();
                }
            }

            ImGui.EndChild();
        }


        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "发布/修改"))
        {
            if (pf.IsVaild())
            {
                pf.LastUpdate = DateTime.Now;
                var result = _apiController.UpdatePFinder(pf).Result;
                if (result)
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        ImGui.SameLine(200f);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, "关闭"))
        {
            if (!string.IsNullOrEmpty(pf.TempGroupPW))
            {
                _ = _apiController.GroupDelete(new GroupDto(pf.Group));
            }
            ImGui.CloseCurrentPopup();
        }
    }

    public void Open(OpenPFinderPopupMessage message)
    {
        pf = message.dto;
        if (pf.EndTime < DateTimeOffset.UtcNow)
        {
            pf.StartTime = DateTimeOffset.UtcNow;
            pf.EndTime = DateTimeOffset.UtcNow;
        }
        pfTitle = pf.Title;
        pfDescription = pf.Description;
        pfIsNsfw = pf.IsNSFW;
        pfTags = pf.Tags;
        pfStartTime = pf.StartTime;
        pfEndTime = pf.EndTime;
        index = 0;
        groups = _pairManager.Groups.Select(x => x.Value)
            .Where(x => (x.GroupUserInfo & GroupPairUserInfo.IsModerator) != 0 || x.OwnerUID == _apiController.UID)
            .ToArray();
        if (string.IsNullOrEmpty(pf.Group.GID))
        {
            pf.Group = new GroupData(groups[index].GID, groups[index].GroupAlias);
        }

        hasTempGroup = pf.HasTempGroup;
    }

/// <summary>
/// 包含一个健壮、易用的、支持 DateTimeOffset 的日期时间选择器 ImGui 控件。
/// </summary>
private static class ImGuiAdvancedWidgets
{
    // 内部状态存储：使用控件的唯一ID来存储其日历视图的状态。
    // static 确保了状态在多次UI帧渲染之间得以保留。
    private static readonly Dictionary<uint, DateTimeOffset> _calendarStates = new();

    /// <summary>
    /// 绘制日历视图。此内部版本现在自己管理日历状态。
    /// </summary>
    private static bool DatePicker(string id, ref DateTimeOffset selectedDate)
    {
        // 获取此控件在当前上下文中的唯一ID
        uint widgetId = ImGui.GetID(id);

        // 从内部字典中获取或创建此控件的日历状态
        if (!_calendarStates.TryGetValue(widgetId, out var calendarState))
        {
            // 如果状态不存在（第一次渲染），则使用当前选中日期的月份作为初始视图
            calendarState = selectedDate;
        }

        bool valueChanged = false;

        ImGui.PushID(id);
        ImGui.BeginGroup();

        if (ImGui.ArrowButton("###left", ImGuiDir.Left))
        {
            calendarState = calendarState.AddMonths(-1);
        }
        ImGui.SameLine();

        ImGui.PushItemWidth(90);
        int year = calendarState.Year;
        string[] monthNames = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
        int month = calendarState.Month - 1;

        if (ImGui.InputInt("###year", ref year, 0))
        {
            year = Math.Clamp(year, 1, 9999);
            calendarState = new DateTimeOffset(year, calendarState.Month, 1, 0, 0, 0, calendarState.Offset);
        }
        ImGui.SameLine();
        if (ImGui.Combo("###month", ref month, monthNames, monthNames.Length))
        {
            calendarState = new DateTimeOffset(calendarState.Year, month + 1, 1, 0, 0, 0, calendarState.Offset);
        }
        ImGui.PopItemWidth();

        ImGui.SameLine();
        if (ImGui.ArrowButton("###right", ImGuiDir.Right))
        {
            calendarState = calendarState.AddMonths(1);
        }

        ImGui.Separator();

        if (ImGui.BeginTable("DatePickerGrid", 7))
        {
            string[] dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            int firstDayOfWeek = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                ImGui.TableSetupColumn(dayNames[(i + firstDayOfWeek) % 7], ImGuiTableColumnFlags.WidthStretch);
            }
            ImGui.TableHeadersRow();

            int daysInMonth = DateTime.DaysInMonth(calendarState.Year, calendarState.Month);
            var firstDayOfMonth = new DateTimeOffset(calendarState.Year, calendarState.Month, 1, 0, 0, 0, calendarState.Offset);
            int startOffset = ((int)firstDayOfMonth.DayOfWeek - firstDayOfWeek + 7) % 7;

            for (int i = 0; i < startOffset; i++) ImGui.TableNextColumn();

            for (int day = 1; day <= daysInMonth; day++)
            {
                ImGui.TableNextColumn();
                bool isSelected = selectedDate.Year == calendarState.Year &&
                                  selectedDate.Month == calendarState.Month &&
                                  selectedDate.Day == day;

                if (isSelected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);

                ImGui.PushID(day);
                if (ImGui.Button($"{day}", new Vector2(-1, 0)))
                {
                    selectedDate = new DateTimeOffset(calendarState.Year, calendarState.Month, day,
                                                      selectedDate.Hour, selectedDate.Minute, selectedDate.Second,
                                                      selectedDate.Offset);
                    valueChanged = true;
                }
                ImGui.PopID();

                if (isSelected) ImGui.PopStyleColor();
            }
            ImGui.EndTable();
        }

        ImGui.EndGroup();
        ImGui.PopID();

        // 将更新后的日历状态存回字典
        _calendarStates[widgetId] = calendarState;

        return valueChanged;
    }

    /// <summary>
    /// 绘制一个完全独立的、支持 DateTimeOffset 的日期时间选择器 (弹出式)。
    /// </summary>
    public static bool DateTimePicker(string id, ref DateTimeOffset dt)
    {
        string popupId = id + "_popup";
        string datePickerId = id + "_date"; // 为子控件定义一个ID
        string displayText = dt.ToString("yyyy-MM-dd HH:mm");

        if (ImGui.Button(displayText + "###" + id))
        {
            ImGui.OpenPopup(popupId);
            // 关键：在打开弹窗时，将日历视图的状态初始化为当前选中的日期。
            // 我们通过子控件的ID来设置它的状态。
            _calendarStates[ImGui.GetID(datePickerId)] = dt;
        }

        bool valueChanged = false;
        if (ImGui.BeginPopup(popupId))
        {
            // 调用内部 DatePicker，它会自己管理状态
            if (DatePicker(datePickerId, ref dt))
            {
                valueChanged = true;
            }
            ImGui.Separator();

            int hour = dt.Hour;
            int minute = dt.Minute;
            int second = dt.Second;
            bool timeUpdated = false;

            ImGui.PushItemWidth(120);
            if (ImGui.DragInt("###hour", ref hour, 1f, 0, 23, "%02d 时")) timeUpdated = true;
            ImGui.SameLine();
            if (ImGui.DragInt("###minute", ref minute, 1f, 0, 59, "%02d 分")) timeUpdated = true;
            ImGui.PopItemWidth();

            if (timeUpdated)
            {
                dt = new DateTimeOffset(dt.Year, dt.Month, dt.Day,
                    Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), 0,
                    dt.Offset);
                valueChanged = true;
            }

            ImGui.EndPopup();
        }

        return valueChanged;
    }

    /// <summary>
    /// 绘制一个方便用户的、在本地时区编辑的日期时间选择器。
    /// </summary>
    public static bool DateTimePickerInLocalZone(string id, ref DateTimeOffset dt)
    {
        var localTime = dt.ToLocalTime();

        bool valueChanged = DateTimePicker(id, ref localTime);

        if (valueChanged)
        {
            dt = localTime.ToUniversalTime();
        }

        return valueChanged;
    }
}
}