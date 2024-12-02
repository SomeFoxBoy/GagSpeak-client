using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using GagSpeak.GagspeakConfiguration.Configurations;
using GagSpeak.GagspeakConfiguration.Models;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.Services.Textures;
using GagSpeak.UI.Handlers;
using GagSpeak.UpdateMonitoring;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Data;
using GagspeakAPI.Data.Character;
using GagspeakAPI.Data.IPC;
using ImGuiNET;
using OtterGui.Classes;
using OtterGui.Text;
using System.Numerics;
using System.Reflection.Metadata;

namespace GagSpeak.UI.UiPuppeteer;

public class PuppeteerUI : WindowMediatorSubscriberBase
{
    private readonly MainHub _apiHubMain;
    private readonly AliasTable _aliasTable;
    private readonly ClientData _clientData;
    private readonly PuppeteerComponents _components;
    private readonly PuppeteerHandler _handler;
    private readonly UserPairListHandler _pairList;
    private readonly ClientConfigurationManager _clientConfigs;
    private readonly ClientMonitorService _clientService;
    private readonly CosmeticService _cosmetics;
    private readonly UiSharedService _uiShared;

    private PuppeteerTab _currentTab = PuppeteerTab.TriggerPhrases;
    private enum PuppeteerTab { TriggerPhrases, ClientAliasList, PairAliasList }

    public PuppeteerUI(ILogger<PuppeteerUI> logger, GagspeakMediator mediator,
        MainHub apiHubMain, AliasTable aliasTable, ClientData clientData,
        PuppeteerComponents components, PuppeteerHandler handler, UserPairListHandler pairList, 
        ClientConfigurationManager clientConfigs, ClientMonitorService clientService,
        CosmeticService cosmetics, UiSharedService uiShared) : base(logger, mediator, "Puppeteer UI")
    {
        _apiHubMain = apiHubMain;
        _aliasTable = aliasTable;
        _clientData = clientData;
        _components = components;
        _handler = handler;
        _pairList = pairList;
        _clientConfigs = clientConfigs;
        _clientService = clientService;
        _cosmetics = cosmetics;
        _uiShared = uiShared;

        AllowPinning = false;
        AllowClickthrough = false;
        // define initial size of window and to not respect the close hotkey.
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(650, 370),
            MaximumSize = new Vector2(1000, float.MaxValue)
        };
        RespectCloseHotkey = false;
    }

    private string AliasSearchString = string.Empty;
    private int LastHoveredIndex = -1;
    private bool isEditingTriggerOptions = false;
    private string UnsavedTriggerPhrase = string.Empty;
    private string UnsavedNewStartChar = string.Empty;
    private string UnsavedNewEndChar = string.Empty;
    private DateTime LastSaveTime = DateTime.MinValue;

    private bool ThemePushed = false;
    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.331f, 0.081f, 0.169f, .803f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.579f, 0.170f, 0.359f, 0.828f));

            ThemePushed = true;
        }
    }

    protected override void PostDrawInternal()
    {
        if (ThemePushed)
        {
            ImGui.PopStyleColor(2);
            ThemePushed = false;
        }
    }

    protected override void DrawInternal()
    {
        // _logger.LogInformation(ImGui.GetWindowSize().ToString()); <-- USE FOR DEBUGGING ONLY.
        // get information about the window region, its item spacing, and the top left side height.
        var region = ImGui.GetContentRegionAvail();
        var itemSpacing = ImGui.GetStyle().ItemSpacing;
        var topLeftSideHeight = region.Y;
        var cellPadding = ImGui.GetStyle().CellPadding;

        // create the draw-table for the selectable and viewport displays
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(5f * _uiShared.GetFontScalerFloat(), 0));
        using var table = ImRaii.Table($"PuppeteerUiWindowTable", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);
        // setup the columns for the table
        ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, 200f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##RightColumn", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextColumn();
        var regionSize = ImGui.GetContentRegionAvail();
        using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f)))
        {
            using (ImRaii.Child($"###PuppeteerLeft", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration))
            {
                var iconTexture = _cosmetics.CorePluginTextures[CorePluginTexture.Logo256];
                if (iconTexture is { } wrap)
                {
                    UtilsExtensions.ImGuiLineCentered("###PuppeteerLogo", () =>
                    {
                        ImGui.Image(wrap.ImGuiHandle, new(125f * _uiShared.GetFontScalerFloat(), 125f * _uiShared.GetFontScalerFloat()));
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text($"What's this? A tooltip hidden in plain sight?");
                            ImGui.EndTooltip();
                        }
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                            UnlocksEventManager.AchievementEvent(UnlocksEvent.EasterEggFound, "Puppeteer");
                    });
                }
                // add separator
                ImGui.Spacing();
                ImGui.Separator();
                float width = ImGui.GetContentRegionAvail().X;

                // show the search filter just above the contacts list to form a nice separation.
                _pairList.DrawSearchFilter(width, ImGui.GetStyle().ItemInnerSpacing.X, false);
                ImGui.Separator();
                using (ImRaii.Child($"###PuppeteerList", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar))
                    _pairList.DrawPairListSelectable(width, true, 2);
            }
        }

        ImGui.TableNextColumn();
        using (ImRaii.Child($"###PuppeteerRightSide", ImGui.GetContentRegionAvail(), false)) 
            DrawPuppeteer(cellPadding);
    }


    // Main Right-half Draw function for puppeteer.
    private void DrawPuppeteer(Vector2 DefaultCellPadding)
    {
        // update the display if we switched selected Pairs.
        if (_handler.SelectedPair is null)
        {
            _uiShared.BigText("Select a Pair to view information!");
            return;
        }

        DrawPuppeteerHeader(DefaultCellPadding);

        ImGui.Separator();

        switch (_currentTab)
        {
            case PuppeteerTab.TriggerPhrases:
                DrawTriggerPhrases();
                break;
            case PuppeteerTab.ClientAliasList:
                DrawClientAliasList(_handler.SelectedPair);
                break;
            case PuppeteerTab.PairAliasList:
                DrawPairAliasList(_handler.SelectedPair);
                break;
        }

        // if the tab is not ClientAliasList and we are editing, cancel the edit.
        if (_currentTab != PuppeteerTab.ClientAliasList && _handler.IsEditingList)
            _handler.CancelEditingList();
    }

    private void DrawPuppeteerHeader(Vector2 DefaultCellPadding)
    {
        if (_handler.SelectedPair is null)
            return;

        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f);
        var startYpos = ImGui.GetCursorPosY();
        Vector2 textSize;
        using (_uiShared.UidFont.Push()) { textSize = ImGui.CalcTextSize("View Info"); }
        var triggerButtonSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.Microphone, "Triggers");
        var clientAliasListSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.EllipsisV, "Your List");
        var pairAliasListSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.EllipsisV, "Pair's List");
        var centerYpos = (textSize.Y - ImGui.GetFrameHeight());

        using (ImRaii.Child("ViewPairInformationHeader", new Vector2(UiSharedService.GetWindowContentRegionWidth(),
            _uiShared.GetIconButtonSize(FontAwesomeIcon.Voicemail).Y + (centerYpos - startYpos) * 2 - DefaultCellPadding.Y)))
        {
            // now next to it we need to draw the header text
            ImGui.SameLine(ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(startYpos);
            using (_uiShared.UidFont.Push())
            {
                UiSharedService.ColorText($"View Info", ImGuiColors.ParsedPink);
            }


            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - triggerButtonSize - clientAliasListSize - pairAliasListSize - ImGui.GetStyle().ItemSpacing.X * 3);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerYpos);
            var currentYpos = ImGui.GetCursorPosY();
            if (_uiShared.IconTextButton(FontAwesomeIcon.Microphone, "Triggers", null, false, _currentTab == PuppeteerTab.TriggerPhrases))
            {
                _currentTab = PuppeteerTab.TriggerPhrases;
                _components.ExpandedAliasItems.Clear();
            }
            UiSharedService.AttachToolTip("View your set trigger phrase, your pairs, and use case examples!");

            // draw revert button at the same location but right below that button
            ImGui.SameLine();
            ImGui.SetCursorPosY(currentYpos);
            if (_uiShared.IconTextButton(FontAwesomeIcon.EllipsisV, "Your List", disabled: _currentTab is PuppeteerTab.ClientAliasList))
            {
                _currentTab = PuppeteerTab.ClientAliasList;
                _components.ExpandedAliasItems.Clear();
            }
            UiSharedService.AttachToolTip("Configure your Alias List.");

            ImGui.SameLine();
            ImGui.SetCursorPosY(currentYpos);
            if (_uiShared.IconTextButton(FontAwesomeIcon.EllipsisV, "Pair's List", disabled: _currentTab == PuppeteerTab.PairAliasList))
            {
                _currentTab = PuppeteerTab.PairAliasList;
                _components.ExpandedAliasItems.Clear();
            }
            UiSharedService.AttachToolTip("View this Pair's Alias List.");
        }
    }

    private void DrawTriggerPhrases()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(5f * _uiShared.GetFontScalerFloat(), 0));
        using var table = ImRaii.Table($"TriggersDisplayForPair", 2, ImGuiTableFlags.BordersInnerV);

        if (!table) return;
        ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, ImGui.GetContentRegionAvail().X / 2);
        ImGui.TableSetupColumn("##RightColumn", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextColumn();

        DrawClientTriggersBox();
        ImGui.TableNextColumn();
        DrawPairTriggersBox();
    }

    private void DrawClientTriggersBox()
    {
        if (_handler.SelectedPair is null)
            return;

        using var windowRounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 5f);
        using var borderColor = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f);
        using var borderCol = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);
        using var bgColor = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.25f, 0.2f, 0.2f, 0.4f));
        // create the child window.
        using (ImRaii.Child("##TriggerDataForClient", new Vector2(ImGui.GetContentRegionAvail().X, 0), true, ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoScrollbar))
        {
            // Draw the client change actions.
            _components.DrawListenerClientGroup(isEditingTriggerOptions,
                (newSits) =>
                {
                    _logger.LogTrace($"Updated AlowSits permission: " + newSits);
                    _ = _apiHubMain.UserUpdateOwnPairPerm(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, new KeyValuePair<string, object>("AllowSitRequests", newSits), UpdateDir.Own));
                },
                (newMotions) =>
                {
                    _logger.LogTrace($"Updated AlowMotions permission: " + newMotions);
                    _ = _apiHubMain.UserUpdateOwnPairPerm(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, new KeyValuePair<string, object>("AllowMotionRequests", newMotions), UpdateDir.Own));
                },
                (newAll) =>
                {
                    _logger.LogTrace($"Updated AlowAll permission: " + newAll);
                    _ = _apiHubMain.UserUpdateOwnPairPerm(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, new KeyValuePair<string, object>("AllowAllRequests", newAll), UpdateDir.Own));
                },
                (newEditState) =>
                {
                    // set the new state, then based on its new state, do things.
                    isEditingTriggerOptions = newEditState;
                    if (newEditState is false)
                    {
                        // save and update our changes.
                        if (!UnsavedTriggerPhrase.IsNullOrEmpty())
                        {
                            _logger.LogTrace($"Updated own pair permission: TriggerPhrase to {UnsavedTriggerPhrase}");
                            _ = _apiHubMain.UserUpdateOwnPairPerm(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, new KeyValuePair<string, object>("TriggerPhrase", UnsavedTriggerPhrase), UpdateDir.Own));
                            UnsavedTriggerPhrase = string.Empty;
                        }
                        if (!UnsavedNewStartChar.IsNullOrEmpty())
                        {
                            _logger.LogTrace($"Updated own pair permission: StartChar to {UnsavedNewStartChar}");
                            _ = _apiHubMain.UserUpdateOwnPairPerm(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, new KeyValuePair<string, object>("StartChar", UnsavedNewStartChar[0]), UpdateDir.Own));
                            UnsavedNewStartChar = string.Empty;
                        }
                        if (!UnsavedNewEndChar.IsNullOrEmpty())
                        {
                            _logger.LogTrace($"Updated own pair permission: EndChar to {UnsavedNewEndChar}");
                            _ = _apiHubMain.UserUpdateOwnPairPerm(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, new KeyValuePair<string, object>("EndChar", UnsavedNewEndChar[0]), UpdateDir.Own));
                            UnsavedNewEndChar = string.Empty;
                        }
                        LastSaveTime = DateTime.Now;
                    }
                    else
                    {
                        UnsavedTriggerPhrase = _handler.SelectedPair.OwnPerms.TriggerPhrase;
                        UnsavedNewStartChar = _handler.SelectedPair.OwnPerms.StartChar.ToString();
                        UnsavedNewEndChar = _handler.SelectedPair.OwnPerms.EndChar.ToString();
                    }
                });

            // setup the listener name if any.
            using (ImRaii.PushFont(UiBuilder.MonoFont)) ImGui.TextUnformatted(_handler.ListenerNameForPair());
            UiSharedService.AttachToolTip("The In Game Character that can use your trigger phrases below on you");

            // draw the trigger phrase box based on if we are editing or not.
            if (isEditingTriggerOptions)
                _components.DrawEditingTriggersWindow(ref UnsavedTriggerPhrase, ref UnsavedNewStartChar, ref UnsavedNewEndChar);
            else
                _components.DrawTriggersWindow(
                    _handler.SelectedPair.OwnPerms.TriggerPhrase,
                    _handler.SelectedPair.OwnPerms.StartChar.ToString(),
                    _handler.SelectedPair.OwnPerms.EndChar.ToString());
        }
    }

    private void DrawPairTriggersBox()
    {
        if(_handler.SelectedPair?.LastAliasData is null || _clientService.ClientPlayer is null)
            return;

        var name = _handler.SelectedPair.UserData.UID;
        // push rounding window corners
        using var windowRounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 5f);
        // push a pink border color for the window border.
        using var borderColor = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f);
        using var borderCol = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);
        // push a less transparent very dark grey background color.
        using var bgColor = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.25f, 0.2f, 0.2f, 0.4f));
        // create the child window.
        using (ImRaii.Child("##TriggerDataForPair" + name, new Vector2(ImGui.GetContentRegionAvail().X, 0), true, ImGuiWindowFlags.ChildWindow))
        {
            // draw the listener top row.
            _components.DrawListenerPairGroup(onSendName: () =>
            {
                var name = _clientService.Name;
                var world = _clientService.HomeWorldId;
                var worldName = OnFrameworkService.WorldData.Value[(ushort)world];
                // compile the alias data to send including our own name and world information, along with an empty alias list.
                var dataToPush = new CharaAliasData()
                {
                    HasNameStored = true,
                    ListenerName = name + "@" + worldName,
                };
                _ = _apiHubMain.UserPushPairDataAliasStorageUpdate(new(_handler.SelectedPair.UserData, MainHub.PlayerUserData, dataToPush, PuppeteerUpdateType.PlayerNameRegistered, UpdateDir.Own));
                _logger.LogDebug("Sent Puppeteer Name to " + _handler.SelectedPair.GetNickAliasOrUid(), LoggerType.Permissions);
            });

            // Draw out the listener name.
            using (ImRaii.PushFont(UiBuilder.MonoFont)) ImGui.TextUnformatted(_handler.SelectedPair.LastAliasData.HasNameStored 
                ? _clientService.ClientPlayer.GetNameWithWorld() : "Not Yet Listening!");

            // Draw the display for trigger phrases.
            _components.DrawTriggersWindow(
                _handler.SelectedPair.PairPerms.TriggerPhrase,
                _handler.SelectedPair.PairPerms.StartChar.ToString(),
                _handler.SelectedPair.PairPerms.EndChar.ToString());
        }
    }

    private void DrawClientAliasList(Pair? pair)
    {
        if(pair is null) return;

        // if we failed to get the storage, create one.
        if(!_clientConfigs.AliasConfig.AliasStorage.TryGetValue(pair.UserData.UID, out var storage))
        {
            _logger.LogDebug("Creating new Alias Storage for " + pair.UserData.UID);
            _clientConfigs.AliasConfig.AliasStorage[pair.UserData.UID] = new AliasStorage();
            // perform a return so the next execution returns true.
            return;
        }

        using (ImRaii.Child("##ClientAliasListChild", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar))
        {
            // Formatting.
            using var windowRounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 5f);
            using var windowPadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8,5));
            using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
            using var borderColor = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f);
            using var borderCol = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);
            using var bgColor = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.25f, 0.2f, 0.2f, 0.4f));

            // Draw out the search filter, then the list of alias's below.
            DrawSearchFilter(ref AliasSearchString, true, _handler.IsEditingList, (onEditToggle) =>
            {
                if (onEditToggle)
                    // we now wish to start editing, so clone the set.
                    _handler.StartEditingList(storage);
                else
                    // if we are not editing, then we should save the changes.
                    _handler.SaveModifiedList();
            });
            ImGui.Separator();

            using var seperatorColor = ImRaii.PushColor(ImGuiCol.Separator, ImGuiColors.ParsedPink);

            var data = _handler.ClonedAliasListForEdit is not null ? _handler.ClonedAliasListForEdit : storage.AliasList;
            List<AliasTrigger> items = data.Where(trigger => trigger.Name.Contains(AliasSearchString, StringComparison.OrdinalIgnoreCase)).ToList();
            var lightSets = _clientConfigs.StoredRestraintSets.Select(x => x.ToLightData()).ToList();
            var ipcData = _clientData.LastIpcData ?? new CharaIPCData();

            for(var i = 0; i < items.Count; i++)
            {
                if (_handler.IsEditingList)
                {
                    _components.DrawAliasItemEditBox(items[i], lightSets, ipcData);
                }
                else
                {
                    _components.DrawAliasItemBox(items[i].AliasIdentifier.ToString()+i, items[i], lightSets, ipcData);
                }
            }
        }
    }

    private void DrawPairAliasList(Pair? pair)
    {
        if(pair is null) return;

        // if any of our data is invalid, do not show.
        if (MainHub.ServerStatus is not ServerState.Connected || pair.LastAliasData is null)
        {
            _uiShared.BigText("Not Connected, or required Data is null!");
            return;
        }

        using (ImRaii.Child("##PairAliasListChild", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar))
        {
            // Formatting.
            using var windowRounding = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 5f);
            using var windowPadding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(8, 5));
            using var framePadding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
            using var borderColor = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f);
            using var borderCol = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);
            using var bgColor = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.25f, 0.2f, 0.2f, 0.4f));

            DrawSearchFilter(ref AliasSearchString, false);
            ImGui.Separator();

            if (pair.LastAliasData.AliasList.Count <= 0)
            {
                _uiShared.GagspeakBigText("No Alias's found from this Kinkster!");
                return;
            }

            using var seperatorColor = ImRaii.PushColor(ImGuiCol.Separator, ImGuiColors.ParsedPink);

            var items = pair.LastAliasData.AliasList.Where(a => a.Name.Contains(AliasSearchString, StringComparison.OrdinalIgnoreCase)).ToList();
            var lightRestraints = pair.LastLightStorage?.Restraints ?? new List<LightRestraintData>();
            var moodlesInfo = pair.LastIpcData ?? new CharaIPCData();

            // Draw out the pairs list
            for (var i = 0; i < items.Count; i++)
            {
                _components.DrawAliasItemBox(items[i].AliasIdentifier.ToString() + i, items[i], lightRestraints, moodlesInfo);
            }
        }
    }

    public void DrawSearchFilter(ref string AliasSearchString, bool showAddEdit, bool isEditing = false, Action<bool>? onEditToggle = null)
    {
        var editSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Edit);
        var addNewSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.Plus, "New Alias");
        var clearSize = _uiShared.GetIconTextButtonSize(FontAwesomeIcon.Ban, "Clear");
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;

        var spaceLeft = showAddEdit 
            ? editSize.X + addNewSize + clearSize + spacing * 3 
            : clearSize + spacing * 2;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - spaceLeft);
        if (ImGui.InputTextWithHint("##AliasSearchStringFilter", "Search for an Alias", ref AliasSearchString, 255))
            LastHoveredIndex = -1;
        ImUtf8.SameLineInner();
        if (_uiShared.IconTextButton(FontAwesomeIcon.Ban, "Clear", disabled: string.IsNullOrEmpty(AliasSearchString)))
        {
            AliasSearchString = string.Empty;
            LastHoveredIndex = -1;
        }
        UiSharedService.AttachToolTip("Clear the search filter.");

        // Dont show the rest if we are not editing.
        if(!showAddEdit)
            return;

        ImUtf8.SameLineInner();
        if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "New Alias", disabled: _handler.ClonedAliasListForEdit is null))
        {
            _logger.LogDebug("Adding new Alias");
            _handler.ClonedAliasListForEdit?.Insert(0, new AliasTrigger());
        }
        UiSharedService.AttachToolTip("Add a new Alias to the list.");

        ImUtf8.SameLineInner();
        using (ImRaii.PushColor(ImGuiCol.Text, isEditing ? ImGuiColors.ParsedPink : ImGuiColors.DalamudWhite))
            if (_uiShared.IconButton(isEditing ? FontAwesomeIcon.Save : FontAwesomeIcon.Edit))
                onEditToggle?.Invoke(!isEditing);
        UiSharedService.AttachToolTip(isEditing ? "Save Changes." : "Start Editing Alias List");
    }
}
