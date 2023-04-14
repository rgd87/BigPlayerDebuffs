﻿using System;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Game.Command;
#if DEBUG
using Dalamud.Logging;
#endif

namespace BigPlayerDebuffs; 

internal enum ChildEnumMode {
    NextNext,
    ChildNext,
    PrevPrev,
    ChildPrev,
    ChildPrevPrev
};

internal enum ChildEnumOrder {
    ZeroForward,
    MaxBackward
}

internal enum ElementType {
    Status,
    FocusStatus,
    TargetStatus
}

internal unsafe class UiElement {
    public readonly string Name;
    public readonly ElementType Type;

    private AtkUnitBase* element;
    private readonly int childListPos;
    private readonly ChildEnumMode enumMode;
    private readonly ChildEnumOrder enumOrder;
    private readonly GameGui gui;

    public void Refresh() {
        element = (AtkUnitBase*) gui.GetAddonByName(Name);
    }

    public UiElement(string name, ElementType type, int childListIndex, ChildEnumMode mode, ChildEnumOrder order, GameGui gameGui) {
        Name = name;
        Type = type;
        childListPos = childListIndex;
        enumMode = mode;
        enumOrder = order;
        gui = gameGui;
        Refresh();
    }

    private bool Valid() => element is not null
                            && element->UldManager.NodeList is not null
                            && element->UldManager.NodeList[childListPos] is not null
                            && element->IsVisible;

    public AtkResNode* StatusList => element->UldManager.NodeList[childListPos];

    public AtkResNode*[] Children {
        get {
            if (!Valid()) {
                return new AtkResNode*[0];
            }
            
            var children = new AtkResNode*[StatusList->ChildCount];
            // TODO: Find a better method for determining child count that applies to both situations
            if (children.Length == 0) {
                // If the nodelist doesn't have a child count, we assume the res node we were given as our starting point
                // is the last node before we begin seeing IconText Component Nodes
                var totalCount = element->UldManager.NodeListCount - childListPos - 1;
                if (totalCount < 0) {
                    return new AtkResNode*[0];
                }
                else {
                    children = new AtkResNode*[totalCount];
                }
            }

            // Separate debuff does it a bit differently :\
            var child = enumMode switch {
                ChildEnumMode.NextNext => StatusList->NextSiblingNode,
                ChildEnumMode.ChildNext => StatusList->ChildNode,
                ChildEnumMode.PrevPrev => StatusList->PrevSiblingNode,
                ChildEnumMode.ChildPrev => StatusList->ChildNode,
                ChildEnumMode.ChildPrevPrev => StatusList->ChildNode->PrevSiblingNode,
                _ => throw new ArgumentOutOfRangeException(nameof(ChildEnumMode),
                    $"Unexpected enum value: {enumMode}")
            };

            // No children? No problem
            if (child is null || (int) child == 0) {
                return new AtkResNode*[0];
            }

            // Reverse for MaxBackward
            var i = enumOrder == ChildEnumOrder.MaxBackward ? children.Length - 1 : 0;

            // soundness (index out of range)
            // will error if the game lies to us about ChildCount
            while (child is not null) {
                var newIndex = enumOrder == ChildEnumOrder.MaxBackward ? i-- : i++;
                #if DEBUG
                try {
                    children[newIndex] = child;
                }
                catch (IndexOutOfRangeException e) {
                    PluginLog.Warning($"Index {i} outside of array with length {children.Length-1} for {Name}");
                }
                #else
                children[newIndex] = child;
                #endif

                child = enumMode switch {
                    ChildEnumMode.NextNext => child->NextSiblingNode,
                    ChildEnumMode.ChildNext => child->NextSiblingNode,
                    ChildEnumMode.PrevPrev => child->PrevSiblingNode,
                    ChildEnumMode.ChildPrev => child->PrevSiblingNode,
                    ChildEnumMode.ChildPrevPrev => child->PrevSiblingNode,
                    _ => throw new ArgumentOutOfRangeException(nameof(ChildEnumMode),
                        $"Unexpected enum value: {enumMode}")
                };
            }

            // Note: The re-sorting we do here lets us avoid annoyances when iterating later
            // because we no longer have to care what nuisances affect accessing the target
            return children;
        }
    }
}

// ReSharper disable once ClassNeverInstantiated.Global
public class BigPlayerDebuffs : IDalamudPlugin {
    public string Name => "BigPlayerDebuffs";

    private readonly DalamudPluginInterface pluginInterface;
    private readonly Framework framework;
    private readonly CommandManager commands;

    private readonly BigPlayerDebuffsConfig pluginConfig;

    private readonly UiElement[] uiElements;

    private bool drawConfigWindow;

    public BigPlayerDebuffs(
        DalamudPluginInterface dalamudPluginInterface,
        CommandManager commandManager,
        Framework dalamudFramework,
        GameGui gameGui
    ) {
        pluginInterface = dalamudPluginInterface;
        commands = commandManager;
        framework = dalamudFramework;

        pluginConfig = pluginInterface.GetPluginConfig() as BigPlayerDebuffsConfig ?? new BigPlayerDebuffsConfig();
        pluginConfig.Init(this, pluginInterface);

        // We have to supply .gui since unsafe classes are static
        uiElements = new[] {
            new UiElement("_TargetInfoBuffDebuff", ElementType.TargetStatus, 1, ChildEnumMode.ChildPrev, ChildEnumOrder.MaxBackward, gameGui),
            new UiElement("_TargetInfo", ElementType.TargetStatus, 2, ChildEnumMode.NextNext, ChildEnumOrder.ZeroForward, gameGui),
            new UiElement("_FocusTargetInfo", ElementType.FocusStatus, 3, ChildEnumMode.NextNext, ChildEnumOrder.ZeroForward, gameGui),
            new UiElement("_Status", ElementType.Status, 0, ChildEnumMode.ChildPrev, ChildEnumOrder.MaxBackward, gameGui),
            new UiElement("_StatusCustom0", ElementType.Status, 4, ChildEnumMode.PrevPrev, ChildEnumOrder.MaxBackward, gameGui),
            new UiElement("_StatusCustom1", ElementType.Status, 4, ChildEnumMode.PrevPrev, ChildEnumOrder.MaxBackward, gameGui),
            new UiElement("_StatusCustom2", ElementType.Status, 4, ChildEnumMode.PrevPrev, ChildEnumOrder.MaxBackward, gameGui),
            new UiElement("_StatusCustom3", ElementType.Status, 3, ChildEnumMode.PrevPrev, ChildEnumOrder.MaxBackward, gameGui)
        };

        // Wire up
        pluginInterface.UiBuilder.Draw += BuildUi;
        pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;
        framework.Update += FrameworkOnUpdate;
        SetupCommands();
    }

    public void Dispose() {
        // Remove hooks
        pluginInterface.UiBuilder.Draw -= BuildUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfig;
        framework.Update -= FrameworkOnUpdate;
        RemoveCommands();
        // Reset changes
        ResetTargetStatus();
    }

    private void FrameworkOnUpdate(Framework _) {
#if DEBUG
        try {
            UpdateTargetStatus();
        }
        catch (Exception ex) {
            PluginLog.Error(ex.ToString());
        }
#else
            UpdateTargetStatus();
#endif
    }


    private unsafe void UpdateTargetStatus() {
        // The actual width and height of the tokens don't matter, they're always 25 apart.
        // e.g. the aspected benefit icon is 24px wide, but the second slot is still at [25, 41]
        const int slotWidth = 25;
        const int slotHeight = 41;


        //PluginLog.Log($"Updating...");
        foreach (var element in uiElements) {
            element.Refresh();
            var playerScale = pluginConfig.bScale;
            switch (element.Type) {
                case ElementType.TargetStatus when pluginConfig.IncludeMainTarget:
                    break;
                case ElementType.FocusStatus when pluginConfig.IncludeFocusTarget:
                    playerScale = pluginConfig.FocusScale;
                    break;
                case ElementType.Status when pluginConfig.IncludeBuffBar:
                    playerScale = pluginConfig.BarScale;
                    break;
                default:
                    continue;
            }

            var children = element.Children;
            var xOffsets = new[] {0f, 0f};
            // Poor man's IEnumerable, but that's life with unsafe
            for (var childIndex = 0; childIndex < children.Length; childIndex++) {
                var child = children[childIndex];
                var textComponent = (AtkTextNode*) child->GetComponent()->GetTextNodeById(2);
                var row = childIndex < 15 ? 0 : 1;
                ref var xOffset = ref xOffsets[row];
                // TODO: find a check that isn't a lazy hack
                var isOwnStatus = textComponent->TextColor is not {R: 255, G: 255, B: 255, A: 255};
                var scalar = isOwnStatus ? playerScale : 1.0f;
                child->ScaleX = scalar;
                child->ScaleY = scalar;
                child->X = childIndex % 15 * slotWidth;
                child->Y = row == 0 ? 0 : slotHeight;
                
                // Add in our running shift value
                child->X += xOffset;
                // If we're on the second row, factor in if the first row has shifted
                if (row > 0 && xOffset > 0f) {
                    child->Y *= playerScale;
                }
                // We bump the Y offset a bit for our changed icons
                if (isOwnStatus) {
                    // Add the difference between an unscaled and a scaled icon to our running total
                    xOffset += slotWidth * playerScale - slotWidth;
                    // Y pos gets shifted slightly to match the top part of the unscaled icons
                    child->Y = (slotHeight * playerScale - slotHeight) / -(slotHeight / 2);
                }

                // Set update flag
                child->Flags_2 |= 0x1;
                // We could step this out but then we have to add null checks and stuff
                element.StatusList->Flags_2 |= 0x4;
                element.StatusList->Flags_2 |= 0x1;
            }
        }
    }

    public unsafe void ResetTargetStatus() {
        foreach (var element in uiElements) {
            element.Refresh();
            var children = element.Children;
            // Poor man's IEnumerable, but that's life with unsafe
            for (var childIndex = 0; childIndex < children.Length; childIndex++) {
                var child = children[childIndex];
                child->ScaleX = 1.0f;
                child->ScaleY = 1.0f;
                child->X = childIndex % 15 * child->Width;
                child->Y = childIndex < 15 ? 0 : child->Height;
                // Set update flag
                child->Flags_2 |= 0x1;
            }

            element.StatusList->Flags_2 |= 0x4;
            element.StatusList->Flags_2 |= 0x1;
        }
    }


    private void SetupCommands() {
        commands.AddHandler("/bigplayerdebuffs", new CommandInfo(OnConfigCommandHandler) {
            HelpMessage = $"Open config window for {Name}",
            ShowInHelp = true
        });
    }

    private void OnOpenConfig() {
        drawConfigWindow = true;
    }

    private void OnConfigCommandHandler(string command, string args) {
        drawConfigWindow = !drawConfigWindow;
    }

    private void RemoveCommands() {
        commands.RemoveHandler("/bigplayerdebuffs");
    }

    private void BuildUi() {
        drawConfigWindow = drawConfigWindow && pluginConfig.DrawConfigUi();
    }
}