using System;
using System.Linq;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;

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

internal unsafe class UiElement {
    public readonly string Name;

    private AtkUnitBase* element;
    private readonly int childListPos;
    private readonly ChildEnumMode enumMode;
    private readonly ChildEnumOrder enumOrder;
    private readonly GameGui gui;

    public void Refresh() {
        element = (AtkUnitBase*) gui.GetAddonByName(Name);
    }

    public UiElement(string name, int childListIndex, ChildEnumMode mode, ChildEnumOrder order, GameGui gameGui) {
        Name = name;
        childListPos = childListIndex;
        enumMode = mode;
        enumOrder = order;
        gui = gameGui;
        Refresh();
    }

    private bool Valid() => element is not null
                            && element->UldManager.NodeList is not null
                            && element->UldManager.NodeList[childListPos] is not null;

    public AtkResNode* StatusList => element->UldManager.NodeList[childListPos];

    public AtkResNode*[] Children {
        get {
            if (!Valid()) {
                return new AtkResNode*[0];
            }

            var children = new AtkResNode*[StatusList->ChildCount];

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
                children[newIndex] = child;

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
    private readonly ClientState client;
    private readonly TargetManager targets;
    private readonly Framework framework;
    private readonly CommandManager commands;

    private readonly BigPlayerDebuffsConfig pluginConfig;

    private readonly UiElement[] uiElements;

    private bool drawConfigWindow;

    private int targetDebuffs = -1;
    private int fTargetDebuffs = -1;

    public BigPlayerDebuffs(
        DalamudPluginInterface dalamudPluginInterface,
        ClientState clientState,
        CommandManager commandManager,
        Framework dalamudFramework,
        GameGui gameGui,
        TargetManager targetManager
    ) {
        pluginInterface = dalamudPluginInterface;
        client = clientState;
        commands = commandManager;
        framework = dalamudFramework;
        targets = targetManager;

        pluginConfig = pluginInterface.GetPluginConfig() as BigPlayerDebuffsConfig ?? new BigPlayerDebuffsConfig();
        pluginConfig.Init(this, pluginInterface);

        // We have to supply .gui since unsafe classes are static
        uiElements = new[] {
            new UiElement("_TargetInfoBuffDebuff", 1, ChildEnumMode.ChildPrev, ChildEnumOrder.MaxBackward, gameGui),
            new UiElement("_TargetInfo", 2, ChildEnumMode.NextNext, ChildEnumOrder.ZeroForward, gameGui),
            new UiElement("_FocusTargetInfo", 3, ChildEnumMode.NextNext, ChildEnumOrder.ZeroForward, gameGui)
        };

        // Wire up
        pluginInterface.UiBuilder.Draw += BuildUi;
        pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;
        framework.Update += FrameworkOnUpdate;
        SetupCommands();
    }

    public void InvalidateState() {
        targetDebuffs = -1;
        UpdateTargetStatus();
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
        var localPlayerId = client.LocalPlayer?.ObjectId;
        var playerAuras = 0;
        // The actual width and height of the tokens don't matter, they're always 25 apart.
        // e.g. the aspected benefit icon is 24px wide, but the second slot is still at [25, 41]
        const int slotWidth = 25;
        const int slotHeight = 41;
        if (targets.Target is BattleChara target) {
            playerAuras = target.StatusList.Count(s => s.SourceId == localPlayerId);
            var x = (FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara*) target.Address;
            x->StatusManager.GetStatusIndex(0x0, target.ObjectId);
        }

        var focusAuras = 0;
        if (targets.FocusTarget is BattleChara focusTarget) {
            focusAuras = focusTarget.StatusList.Count(s => s.SourceId == localPlayerId);
        }

        //PluginLog.Log($"StatusEffects.Length {target.StatusEffects.Length}"); // Always 30
        //PluginLog.Log($"Player Auras old:{this.curDebuffs} new: {playerAuras}");
        // Hasn't changed since last tick
        if (targetDebuffs == playerAuras && fTargetDebuffs == focusAuras) {
            return;
        }

        // Update our counters
        targetDebuffs = playerAuras;
        fTargetDebuffs = focusAuras;


        //PluginLog.Log($"Updating...");
        foreach (var element in uiElements) {
            element.Refresh();
            var targetAuras = playerAuras;
            var playerScale = pluginConfig.bScale;
            switch (element.Name) {
                case "_TargetInfoBuffDebuff" when pluginConfig.IncludeMainTarget:
                    break;
                case "_TargetInfo" when pluginConfig.IncludeMainTarget:
                    break;
                case "_FocusTargetInfo" when pluginConfig.IncludeFocusTarget:
                    targetAuras = focusAuras;
                    playerScale = pluginConfig.FocusScale;
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
                // Add in our running shift value
                child->X = childIndex % 15 * slotWidth + xOffset;
                child->Y = row == 0 ? 0 : slotHeight;
                
                // We actually have work to do?
                if (targetAuras > 0) {
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