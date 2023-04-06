using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using System.Reflection;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Data;
using Dalamud.Logging;

namespace BigPlayerDebuffs
{
    internal unsafe class Common {
        public static DalamudPluginInterface PluginInterface { get; private set; }
        public static GameGui GameGui { get; private set; }

        public Common(DalamudPluginInterface pluginInterface, GameGui gameGui)
        {
            PluginInterface = pluginInterface;
            GameGui = gameGui;
        }
        public static AtkUnitBase* GetUnitBase(string name, int index = 1)
        {
            return (AtkUnitBase*)GameGui.GetAddonByName(name, index);
        }

        public static T* GetUnitBase<T>(string name = null, int index = 1) where T : unmanaged
        {
            if (string.IsNullOrEmpty(name))
            {
                var attr = (Addon)typeof(T).GetCustomAttribute(typeof(Addon));
                if (attr != null)
                {
                    name = attr.AddonIdentifiers.FirstOrDefault();
                }
            }

            if (string.IsNullOrEmpty(name)) return null;

            return (T*)GameGui.GetAddonByName(name, index);
        }
    }

    public class BigPlayerDebuffs: IDalamudPlugin {
        public string Name => "BigPlayerDebuffs";

        public static DalamudPluginInterface PluginInterface { get; private set; }
        public static ClientState ClientState { get; private set; }
        public static TargetManager TargetManager{ get; private set; }
        public static Framework Framework { get; private set; }
        public static GameGui GameGui { get; private set; }
        public static CommandManager CommandManager { get; private set; }
        public static ObjectTable Objects { get; private set; }
        public static SigScanner SigScanner { get; private set; }
        public static DataManager DataManager { get; private set; }

        public BigPlayerDebuffsConfig PluginConfig { get; private set; }

        private bool drawConfigWindow;

        internal Common common;

        int curSecondRowOffset = 41;
        int targetDebuffs = -1;
        private int fTargetDebuffs = -1;

        public BigPlayerDebuffs(
                DalamudPluginInterface pluginInterface,
                ClientState clientState,
                CommandManager commandManager,
                Framework framework,
                ObjectTable objects,
                GameGui gameGui,
                SigScanner sigScanner,
                DataManager dataManager,
                TargetManager targets
            )
        {
            PluginInterface = pluginInterface;
            ClientState = clientState;
            Framework = framework;
            CommandManager = commandManager;
            Objects = objects;
            SigScanner = sigScanner;
            DataManager = dataManager;
            TargetManager = targets;

            this.common = new Common(pluginInterface, gameGui);

            this.PluginConfig = (BigPlayerDebuffsConfig)pluginInterface.GetPluginConfig() ?? new BigPlayerDebuffsConfig();
            this.PluginConfig.Init(this, pluginInterface);

            // Upgrade if config is too old
            //try
            //{
            //    Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            //}
            //catch (Exception e)
            //{
            //    PluginLog.LogError("Error loading config", e);
            //    Config = new Configuration();
            //    Config.Save();
            //}
            //if (Config.Version == 0)
            //{
            //    PluginLog.Log("Old config version found");
            //    Config = new Configuration();
            //    Config.Save();
            //}

            PluginInterface.UiBuilder.Draw += this.BuildUI;
            PluginInterface.UiBuilder.OpenConfigUi += this.OnOpenConfig;
            Framework.Update += FrameworkOnUpdate;
            SetupCommands();

        }

        public void InvalidateState()
        {
            targetDebuffs = -1;
            curSecondRowOffset = -1;
            UpdateTargetStatus();
        }


        public void Dispose() {
            PluginInterface.UiBuilder.Draw -= this.BuildUI;
            PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfig;

            Framework.Update -= FrameworkOnUpdate;

            ResetTargetStatus();

            PluginInterface = null;
            //Config = null;

            RemoveCommands();
        }

        private void FrameworkOnUpdate(Framework framework)
        {
#if DEBUG
            try
            {
                UpdateTargetStatus();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex.ToString());
            }
#else
            UpdateTargetStatus();
#endif
        }

        private enum ChildEnumMode {
            NextNext,
            ChildNext,
            PrevPrev,
            ChildPrev,
            ChildPrevPrev
        };

        private enum ChildEnumOrder {
            ZeroForward,
            MaxBackward
        }

        private unsafe struct UiElement {
            public readonly AtkUnitBase* Element;
            public readonly string Name;
            public readonly int StatusListIndex;
            public readonly ChildEnumMode EnumMode;
            public readonly ChildEnumOrder EnumOrder;

            public UiElement(string name, int statusListIndex) {
                Name = name;
                Element = Common.GetUnitBase(name);
                StatusListIndex = statusListIndex;
                EnumMode = Name == "_TargetInfoBuffDebuff" ? ChildEnumMode.ChildPrev : ChildEnumMode.NextNext;
                EnumOrder = Name == "_TargetInfoBuffDebuff" ? ChildEnumOrder.MaxBackward : ChildEnumOrder.ZeroForward;
            }

            public bool Valid() => Element is not null
                                   && Element->UldManager.NodeList is not null
                                   && Element->UldManager.NodeList[StatusListIndex] is not null;

            public AtkResNode* StatusList => Element->UldManager.NodeList[StatusListIndex];

            public AtkResNode*[] Children {
                get {
                    if (!Valid())
                        return new AtkResNode*[0];
                    var children = new AtkResNode*[StatusList->ChildCount];
                    
                    // Separate debuff does it a bit differently :\
                    var child = EnumMode switch {
                        ChildEnumMode.NextNext => StatusList->NextSiblingNode,
                        ChildEnumMode.ChildNext => StatusList->ChildNode,
                        ChildEnumMode.PrevPrev => StatusList->PrevSiblingNode,
                        ChildEnumMode.ChildPrev => StatusList->ChildNode,
                        ChildEnumMode.ChildPrevPrev => StatusList->ChildNode->PrevSiblingNode,
                        _ => throw new ArgumentOutOfRangeException(nameof(ChildEnumMode), $"Unexpected enum value: {EnumMode}")
                    };

                    // No children? No problem
                    if (child is null || (int) child == 0)
                        return new AtkResNode*[0];
                    // Reverse for MaxBackward
                    var i = EnumOrder == ChildEnumOrder.MaxBackward ? children.Length - 1 : 0;
                    
                    // soundness (index out of range)
                    // will error if the game lies to us about ChildCount
                    while (child is not null) {
                        var newIndex = EnumOrder == ChildEnumOrder.MaxBackward ? i-- : i++;
                        children[newIndex] = child;

                        child = EnumMode switch {
                            ChildEnumMode.NextNext => child->NextSiblingNode,
                            ChildEnumMode.ChildNext => child->NextSiblingNode,
                            ChildEnumMode.PrevPrev => child->PrevSiblingNode,
                            ChildEnumMode.ChildPrev => child->PrevSiblingNode,
                            ChildEnumMode.ChildPrevPrev => child->PrevSiblingNode,
                            _ => throw new ArgumentOutOfRangeException(nameof(ChildEnumMode), $"Unexpected enum value: {EnumMode}")
                        };
                    }
                    
                    // Note: The re-sorting we do here lets us avoid annoyances when iterating later
                    // because we no longer have to care what nuisances affect accessing the target
                    return children;
                }
            }
        }

        private readonly Dictionary<int, string> targetElements = new(){
            {1, "_TargetInfoBuffDebuff"},
            {2, "_TargetInfo"},
            {3, "_FocusTargetInfo"}
        };

        private unsafe void UpdateTargetStatus() {

            var localPlayerId = ClientState.LocalPlayer?.ObjectId;
            var playerAuras = 0;
            if (TargetManager.Target is BattleChara target) {
                playerAuras = target.StatusList.Count(s => s.SourceId == localPlayerId);
            }

            var focusAuras = 0;
            if (TargetManager.FocusTarget is BattleChara focusTarget) {
                focusAuras = focusTarget.StatusList.Count(s => s.SourceId == localPlayerId);
            }
            
            //PluginLog.Log($"StatusEffects.Length {target.StatusEffects.Length}"); // Always 30
            //PluginLog.Log($"Player Auras old:{this.curDebuffs} new: {playerAuras}");
            // Hasn't changed since last tick
            if (targetDebuffs == playerAuras && fTargetDebuffs == focusAuras) {
                return;
            }

            //PluginLog.Log($"Updating...");
            foreach (var element in targetElements) {
                var playerScale = PluginConfig.bScale;
                var targetAuras = playerAuras;
                switch (element.Value) {
                    case "_TargetInfoBuffDebuff" when PluginConfig.includeMainTarget:
                        break;
                    case "_TargetInfo" when PluginConfig.includeMainTarget:
                        break;
                    case "_FocusTargetInfo" when PluginConfig.includeFocusTarget:
                        playerScale = PluginConfig.fScale;
                        targetAuras = focusAuras;
                        break;
                    default:
                        continue;
                }
                var uiElement = new UiElement(element.Value, element.Key);
                var children = uiElement.Children;
                // Poor man's IEnumerable, but that's life with unsafe
                for (var childIndex = 0; childIndex < children.Length; childIndex++) {
                    var child = children[childIndex];
                    var scalar =  childIndex < targetAuras ? playerScale : 1.0f;
                    child->ScaleX = scalar;
                    child->ScaleY = scalar;
                    child->X = childIndex % 15 * child->Width;
                    child->Y = childIndex < 15 ? 0 : child->Height;
                    if (childIndex < targetAuras) {
                        child->ScaleX = playerScale;
                        child->ScaleY = playerScale;
                    }
                    switch (childIndex) {
                        // For simplicity's sake, we're going to assume no player has >14 debuffs out at once
                        case < 15 when targetAuras > 0:
                            // Add the difference between an unscaled and a scaled icon
                            child->X += (child->Width * playerScale - child->Width) * MathF.Min(childIndex, targetAuras);
                            // We bump the Y offset a bit for our changed icons
                            if (childIndex < targetAuras) {
                                child->Y = (child->Height * playerScale - child->Height) / -(child->Height / 2 );
                            }
                            else {
                                child->Y = 0;
                            }
                            break;
                        case > 14 when targetAuras > 0:
                            child->Y = child->Height * playerScale;
                            break;
                        default:
                            child->Y = childIndex > 14 ? child->Height * playerScale : 0;
                            break;
                    }
                    // Set update flag
                    child->Flags_2 |= 0x1;
                    // Onto the next one
                    child = child->NextSiblingNode;
                }
                uiElement.StatusList->Flags_2 |= 0x4;
                uiElement.StatusList->Flags_2 |= 0x1;
            }
        }

        public unsafe void ResetTargetStatus()
        {
            foreach (var element in targetElements) {
                var uiElement = new UiElement(element.Value, element.Key);
                var children = uiElement.Children;
                // Poor man's IEnumerable, but that's life with unsafe
                for(var childIndex = 0; childIndex < children.Length; childIndex++) {
                    var child = children[childIndex];
                    child->ScaleX = 1.0f;
                    child->ScaleY = 1.0f;
                    child->X = childIndex % 15 * child->Width;
                    child->Y = childIndex < 15 ? 0 : child->Height;
                    // Set update flag
                    child->Flags_2 |= 0x1;
                    // Onto the next one
                    child = child->NextSiblingNode;
                }

                uiElement.StatusList->Flags_2 |= 0x4;
                uiElement.StatusList->Flags_2 |= 0x1;
            }
        }



        public void SetupCommands() {
            CommandManager.AddHandler("/bigplayerdebuffs", new Dalamud.Game.Command.CommandInfo(OnConfigCommandHandler) {
                HelpMessage = $"Open config window for {this.Name}",
                ShowInHelp = true
            });
        }

        private void OnOpenConfig()
        {
            drawConfigWindow = true;
        }

        public void OnConfigCommandHandler(string command, string args) {
            drawConfigWindow = !drawConfigWindow;
        }

        public void RemoveCommands() {
            CommandManager.RemoveHandler("/bigplayerdebuffs");
        }

        private void BuildUI() {
            drawConfigWindow = drawConfigWindow && PluginConfig.DrawConfigUI();
        }
    }
}
