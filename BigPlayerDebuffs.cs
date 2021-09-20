using System;
using System.Linq;
using Dalamud.Game.Internal;
using Dalamud.Plugin;
using System.Reflection;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Component.GUI;


namespace BigPlayerDebuffs
{
    internal unsafe class Common {
        public static DalamudPluginInterface PluginInterface { get; private set; }

        public Common(DalamudPluginInterface pluginInterface)
        {
            PluginInterface = pluginInterface;
        }
        public static AtkUnitBase* GetUnitBase(string name, int index = 1)
        {
            return (AtkUnitBase*)PluginInterface.Framework.Gui.GetUiObjectByName(name, index);
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

            return (T*)PluginInterface.Framework.Gui.GetUiObjectByName(name, index);
        }
    }

    public class BigPlayerDebuffs: IDalamudPlugin {
        public string Name => "Big Player Debuffs";
        public DalamudPluginInterface PluginInterface { get; private set; }
        public BigPlayerDebuffsConfig PluginConfig { get; private set; }

        private bool drawConfigWindow;

        internal Common common;

        int curSecondRowOffset = 41;
        int curDebuffs = -1;

        public void InvalidateState()
        {
            curDebuffs = -1;
            curSecondRowOffset = -1;
            UpdateTargetStatus();
        }


        public void Dispose() {
            PluginInterface.UiBuilder.OnBuildUi -= this.BuildUI;

            PluginInterface.Framework.OnUpdateEvent -= FrameworkOnUpdate;

            ResetTargetStatus();

            RemoveCommands();
        }

        public void Initialize(DalamudPluginInterface pluginInterface) {
            this.PluginInterface = pluginInterface;
            this.PluginConfig = (BigPlayerDebuffsConfig) pluginInterface.GetPluginConfig() ?? new BigPlayerDebuffsConfig();
            this.PluginConfig.Init(this, pluginInterface);

            this.common = new Common(pluginInterface);


            PluginInterface.UiBuilder.OnOpenConfigUi += (sender, args) => {
                this.drawConfigWindow = true;
            };

            PluginInterface.UiBuilder.OnBuildUi += this.BuildUI;


            PluginInterface.Framework.OnUpdateEvent += FrameworkOnUpdate;

            SetupCommands();
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

        private unsafe void UpdateTargetStatus()
        {
            var target = this.PluginInterface.ClientState.Targets.CurrentTarget;
            if (target != null)
            {
                var playerAuras = 0;

                //PluginLog.Log($"StatusEffects.Length {target.StatusEffects.Length}"); // Always 30

                var localPlayerId = this.PluginInterface.ClientState.LocalPlayer?.ActorId;
                for (var i = 0; i < 30; i++)
                {
                    if (target.StatusEffects[i].OwnerId == localPlayerId) playerAuras++;
                }

                //PluginLog.Log($"Player Auras {playerAuras}");

                if (this.curDebuffs != playerAuras) {

                    var playerScale = this.PluginConfig.bScale;

                    var targetInfoUnitBase = Common.GetUnitBase("_TargetInfo", 1);
                    if (targetInfoUnitBase == null) return;
                    if (targetInfoUnitBase->UldManager.NodeList == null || targetInfoUnitBase->UldManager.NodeListCount < 53) return;

                    var targetInfoStatusUnitBase = Common.GetUnitBase("_TargetInfoBuffDebuff", 1);
                    if (targetInfoStatusUnitBase == null) return;
                    if (targetInfoStatusUnitBase->UldManager.NodeList == null || targetInfoStatusUnitBase->UldManager.NodeListCount < 32) return;

                    this.curDebuffs = playerAuras;

                    var adjustOffsetY = -(int)(41 * (playerScale-1.0f)/4.5);

                    var xIncrement = (int)((playerScale - 1.0f) * 25);

                    // Split Target Frame

                    var growingOffsetX = 0;
                    for (var i = 0; i < 15; i++)
                    {
                        var node = targetInfoStatusUnitBase->UldManager.NodeList[31 - i];
                        node->X = i * 25 + growingOffsetX;

                        if (i < playerAuras)
                        {
                            node->ScaleX = playerScale;
                            node->ScaleY = playerScale;
                            node->Y = adjustOffsetY;
                            growingOffsetX += xIncrement;
                        }
                        else
                        {
                            node->ScaleX = 1.0f;
                            node->ScaleY = 1.0f;
                            node->Y = 0;
                        }
                        node->Flags_2 |= 0x1; // 0x1 flag i'm guessing recalculates only for this node
                    }

                    // Merged Target Frame

                    growingOffsetX = 0;
                    for (var i = 0; i < 15; i++)
                    {
                        var node = targetInfoUnitBase->UldManager.NodeList[32 - i];
                        node->X = i * 25 + growingOffsetX;

                        if (i < playerAuras)
                        {
                            node->ScaleX = playerScale;
                            node->ScaleY = playerScale;
                            node->Y = adjustOffsetY;
                            growingOffsetX += xIncrement;
                        }
                        else
                        {
                            node->ScaleX = 1.0f;
                            node->ScaleY = 1.0f;
                            node->Y = 0;
                        }
                        node->Flags_2 |= 0x1;
                    }

                    ///////////////////

                    var newSecondRowOffset = (playerAuras > 0) ? (int)(playerScale*41) : 41;

                    if (newSecondRowOffset != this.curSecondRowOffset)
                    {
                        // Split Target Frame Second Row
                        for (var i = 17; i >= 2; i--)
                        {
                            targetInfoStatusUnitBase->UldManager.NodeList[i]->Y = newSecondRowOffset;
                            targetInfoStatusUnitBase->UldManager.NodeList[i]->Flags_2 |= 0x1;
                        }
                        // Merged Target Frame Second Row
                        for (var i = 18; i >= 3; i--)
                        {
                            targetInfoUnitBase->UldManager.NodeList[i]->Y = newSecondRowOffset;
                            targetInfoUnitBase->UldManager.NodeList[i]->Flags_2 |= 0x1;
                        }
                        this.curSecondRowOffset = newSecondRowOffset;
                    }

                    // Setting 0x4 flag on the root element to recalculate the scales down the tree
                    targetInfoStatusUnitBase->UldManager.NodeList[1]->Flags_2 |= 0x4;
                    targetInfoStatusUnitBase->UldManager.NodeList[1]->Flags_2 |= 0x1;
                    targetInfoUnitBase->UldManager.NodeList[2]->Flags_2 |= 0x4;
                    targetInfoUnitBase->UldManager.NodeList[2]->Flags_2 |= 0x1;

                }
            }

        }

        private unsafe void ResetTargetStatus()
        {
            var targetInfoUnitBase = Common.GetUnitBase("_TargetInfo", 1);
            if (targetInfoUnitBase == null) return;
            if (targetInfoUnitBase->UldManager.NodeList == null || targetInfoUnitBase->UldManager.NodeListCount < 53) return;

            var targetInfoStatusUnitBase = Common.GetUnitBase("_TargetInfoBuffDebuff", 1);
            if (targetInfoStatusUnitBase == null) return;
            if (targetInfoStatusUnitBase->UldManager.NodeList == null || targetInfoStatusUnitBase->UldManager.NodeListCount < 32) return;

            for (var i = 0; i < 15; i++)
            {
                var node = targetInfoStatusUnitBase->UldManager.NodeList[31 - i];
                node->ScaleX = 1.0f;
                node->ScaleY = 1.0f;
                node->X = i * 25;
                node->Y = 0;
                node->Flags_2 |= 0x1;

                node = targetInfoUnitBase->UldManager.NodeList[32 - i];
                node->ScaleX = 1.0f;
                node->ScaleY = 1.0f;
                node->X = i * 25;
                node->Y = 0;
                node->Flags_2 |= 0x1;
            }
            for (var i = 17; i >= 2; i--)
            {
                targetInfoStatusUnitBase->UldManager.NodeList[i]->Y = 41;
                targetInfoStatusUnitBase->UldManager.NodeList[i]->Flags_2 |= 0x1;
            }
            for (var i = 18; i >= 3; i--)
            {
                targetInfoUnitBase->UldManager.NodeList[i]->Y = 41;
                targetInfoUnitBase->UldManager.NodeList[i]->Flags_2 |= 0x1;
            }

            targetInfoStatusUnitBase->UldManager.NodeList[1]->Flags_2 |= 0x4;
            targetInfoUnitBase->UldManager.NodeList[2]->Flags_2 |= 0x4;
        }



        public void SetupCommands() {
            PluginInterface.CommandManager.AddHandler("/bigplayerdebuffs", new Dalamud.Game.Command.CommandInfo(OnConfigCommandHandler) {
                HelpMessage = $"Open config window for {this.Name}",
                ShowInHelp = true
            });
        }

        public void OnConfigCommandHandler(string command, string args) {
            drawConfigWindow = !drawConfigWindow;
        }

        public void RemoveCommands() {
            PluginInterface.CommandManager.RemoveHandler("/bigplayerdebuffs");
        }

        private void BuildUI() {
            drawConfigWindow = drawConfigWindow && PluginConfig.DrawConfigUI();
        }
    }
}
