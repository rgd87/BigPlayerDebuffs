using Dalamud.Configuration;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Game.Text;

namespace BigPlayerDebuffs
{
    public class RouletteConfig {
        public bool Enabled;
        public bool Tank;
        public bool Healer;
        public bool DPS;
    }

    public class BigPlayerDebuffsConfig : IPluginConfiguration {
        [NonSerialized]
        private DalamudPluginInterface pluginInterface;

        [NonSerialized]
        private BigPlayerDebuffs plugin;

        //[NonSerialized] private bool showWebhookWindow;

        public int Version { get; set; }

        public float bScale = 1.4f;


        public void Init(BigPlayerDebuffs plugin, DalamudPluginInterface pluginInterface) {
            this.plugin = plugin;
            this.pluginInterface = pluginInterface;
        }

        public void Save() {
            pluginInterface.SavePluginConfig(this);
            plugin.InvalidateState();
        }

        public bool DrawConfigUI() {
            var drawConfig = true;

            var scale = ImGui.GetIO().FontGlobalScale;

            var modified = false;

            ImGui.SetNextWindowSize(new Vector2(500 * scale, 350), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(500 * scale, 350), new Vector2(560 * scale, 650));
            ImGui.Begin($"{plugin.Name} Config", ref drawConfig, ImGuiWindowFlags.NoCollapse);

            modified |= ImGui.SliderFloat("Own Buff/Debuff Scale", ref bScale, 1.0F, 2.0F);

            ImGui.End();


            if (modified)
            {
                Save();
            }

            return drawConfig;
        }
    }
}