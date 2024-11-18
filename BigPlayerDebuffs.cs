using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Reflection;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Component.GUI;

using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Data;

namespace BigPlayerDebuffs
{
    internal unsafe class Common {
        public static IDalamudPluginInterface PluginInterface { get; private set; }
        public static IGameGui GameGui { get; private set; }

        public Common(IDalamudPluginInterface pluginInterface, IGameGui gameGui)
        {
            PluginInterface = pluginInterface;
            GameGui = gameGui;
        }
        public static AtkUnitBase* GetUnitBase(string name, int index = 1)
        {
            return (AtkUnitBase*)GameGui.GetAddonByName(name, index);
        }
        /*
        public static T* GetUnitBase<T>(string name = null, int index = 1) where T : unmanaged
        {
            if (string.IsNullOrEmpty(name))
            {
                var attr = (AddonAttribute)typeof(T).GetCustomAttribute(typeof(AddonAttribute));
                if (attr != null)
                {
                    name = attr.AddonIdentifiers.FirstOrDefault();
                }
            }

            if (string.IsNullOrEmpty(name)) return null;

            return (T*)GameGui.GetAddonByName(name, index);
        }*/
    }

    public class BigPlayerDebuffs: IDalamudPlugin {
        public string Name => "BigPlayerDebuffs";

        public static IDalamudPluginInterface PluginInterface { get; private set; }
        public static IClientState ClientState { get; private set; }
        public static ITargetManager TargetManager{ get; private set; }
        public static IFramework Framework { get; private set; }
        public static IGameGui GameGui { get; private set; }
        public static ICommandManager CommandManager { get; private set; }
        public static IObjectTable Objects { get; private set; }
        public static ISigScanner SigScanner { get; private set; }
        public static IDataManager DataManager { get; private set; }

        public BigPlayerDebuffsConfig PluginConfig { get; private set; }

        private bool drawConfigWindow;

        internal Common common;

        int curSecondRowOffset = 41;
        int curDebuffs = -1;

        public BigPlayerDebuffs(
                IDalamudPluginInterface pluginInterface,
                IClientState clientState,
                ICommandManager commandManager,
                IFramework framework,
                IObjectTable objects,
                IGameGui gameGui,
                ISigScanner sigScanner,
                IDataManager dataManager,
                ITargetManager targets
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
            // Adds another button that is doing the same but for the main ui of the plugin
            PluginInterface.UiBuilder.OpenMainUi += this.OnOpenConfig;
            Framework.Update += FrameworkOnUpdate;
            SetupCommands();

        }

        public void InvalidateState()
        {
            curDebuffs = -1;
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

        //public void Initialize(DalamudPluginInterface pluginInterface) {
        //    this.PluginInterface = pluginInterface;
        //    this.PluginConfig = (BigPlayerDebuffsConfig) pluginInterface.GetPluginConfig() ?? new BigPlayerDebuffsConfig();
        //    this.PluginConfig.Init(this, pluginInterface);

        //    this.common = new Common(pluginInterface);


        //    PluginInterface.UiBuilder.OnOpenConfigUi += (sender, args) => {
        //        this.drawConfigWindow = true;
        //    };

        //    PluginInterface.UiBuilder.OnBuildUi += this.BuildUI;


        //    PluginInterface.Framework.OnUpdateEvent += FrameworkOnUpdate;

        //    SetupCommands();
        //}

        private void FrameworkOnUpdate(IFramework framework)
        {
#if DEBUG
            try
            {
                UpdateTargetStatus();
            }
            catch (Exception ex)
            {
                IPluginLog.Error(ex.ToString());
            }
#else
            UpdateTargetStatus();
#endif
        }

        private unsafe void UpdateTargetStatus()
        {
            
            //var targetGameObject = TargetManager.Target;
            if (TargetManager.Target is IBattleChara target)
            {
                 
                var playerAuras = 0;

                //PluginLog.Log($"StatusEffects.Length {target.StatusEffects.Length}"); // Always 30

                var localPlayerId = ClientState.LocalPlayer?.GameObjectId;
                for (var i = 0; i < 30; i++)
                {
                    if (target.StatusList[i].SourceId == localPlayerId) playerAuras++;
                }

                //PluginLog.Log($"Player Auras old:{this.curDebuffs} new: {playerAuras}");

                if (this.curDebuffs != playerAuras) {

                    //PluginLog.Log($"Updating...");

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
                        node->DrawFlags |= 0x1; // 0x1 flag i'm guessing recalculates only for this node
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
                        node->DrawFlags |= 0x1;
                    }

                    ///////////////////

                    var newSecondRowOffset = (playerAuras > 0) ? (int)(playerScale*41) : 41;

                    if (newSecondRowOffset != this.curSecondRowOffset)
                    {
                        // Split Target Frame Second Row
                        for (var i = 16; i >= 2; i--)
                        {
                            targetInfoStatusUnitBase->UldManager.NodeList[i]->Y = newSecondRowOffset;
                            targetInfoStatusUnitBase->UldManager.NodeList[i]->DrawFlags |= 0x1;
                        }
                        // Merged Target Frame Second Row
                        for (var i = 17; i >= 3; i--)
                        {
                            targetInfoUnitBase->UldManager.NodeList[i]->Y = newSecondRowOffset;
                            targetInfoUnitBase->UldManager.NodeList[i]->DrawFlags |= 0x1;
                        }
                        this.curSecondRowOffset = newSecondRowOffset;
                    }

                    // Setting 0x4 flag on the root element to recalculate the scales down the tree
                    targetInfoStatusUnitBase->UldManager.NodeList[1]->DrawFlags |= 0x4;
                    targetInfoStatusUnitBase->UldManager.NodeList[1]->DrawFlags |= 0x1;
                    targetInfoUnitBase->UldManager.NodeList[2]->DrawFlags |= 0x4;
                    targetInfoUnitBase->UldManager.NodeList[2]->DrawFlags |= 0x1;

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
                node->DrawFlags |= 0x1;

                node = targetInfoUnitBase->UldManager.NodeList[32 - i];
                node->ScaleX = 1.0f;
                node->ScaleY = 1.0f;
                node->X = i * 25;
                node->Y = 0;
                node->DrawFlags |= 0x1;
            }
            for (var i = 17; i >= 2; i--)
            {
                targetInfoStatusUnitBase->UldManager.NodeList[i]->Y = 41;
                targetInfoStatusUnitBase->UldManager.NodeList[i]->DrawFlags |= 0x1;
            }
            for (var i = 18; i >= 3; i--)
            {
                targetInfoUnitBase->UldManager.NodeList[i]->Y = 41;
                targetInfoUnitBase->UldManager.NodeList[i]->DrawFlags |= 0x1;
            }

            targetInfoStatusUnitBase->UldManager.NodeList[1]->DrawFlags |= 0x4;
            targetInfoUnitBase->UldManager.NodeList[2]->DrawFlags |= 0x4;
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
