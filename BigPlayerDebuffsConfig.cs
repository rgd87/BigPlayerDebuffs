using Dalamud.Configuration;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;

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

        // What does the 'b' mean?
        public float bScale = 1.4f;
        public float fScale = 1.25f;
        public bool includeMainTarget = true;
        public bool includeFocusTarget = true;

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

            ImGui.SetNextWindowSize(new Vector2(550, 120) * scale, ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(550, 110), new Vector2(1100, 650) * scale);
            ImGui.Begin($"{plugin.Name} Configuration", ref drawConfig, ImGuiWindowFlags.NoCollapse);
            // Target UI
            ImGui.BeginGroup();
            modified |= ImGui.Checkbox("##Enable scaling in Target UI",ref includeMainTarget);
            if(ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable scaling in target UI");
            ImGui.SameLine();
            ImGui.BeginDisabled(!includeMainTarget);
            modified |= ImGui.SliderFloat("Scale in Target UI", ref bScale, 1.0F, 2.0F, "%.2f");
            ImGui.EndDisabled();
            ImGui.EndGroup();
            // Focus target
            ImGui.BeginGroup();
            modified |= ImGui.Checkbox("##Enable scaling in Focus Target UI",ref includeFocusTarget);
            if(ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable scaling in focus target UI");
            ImGui.SameLine();
            ImGui.BeginDisabled(!includeFocusTarget);
            modified |= ImGui.SliderFloat("Scale in Focus Target UI", ref fScale, 1.0F, 2.0F, "%.2f");
            ImGui.EndDisabled();
            ImGui.EndGroup();
            ImGui.Text("Hint: Ctrl+Click a slider to input a number directly");
            ImGui.End();


            if (modified)
            {
                plugin.ResetTargetStatus();
                Save();
            }

            return drawConfig;
        }
    }
}