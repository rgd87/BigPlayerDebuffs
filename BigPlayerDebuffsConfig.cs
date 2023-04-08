using Dalamud.Configuration;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;

namespace BigPlayerDebuffs; 

public class BigPlayerDebuffsConfig : IPluginConfiguration {
    [NonSerialized] private DalamudPluginInterface pluginInterface = null!;

    [NonSerialized] private BigPlayerDebuffs plugin = null!;

    //[NonSerialized] private bool showWebhookWindow;

    public int Version { get; set; }

    // What does the 'b' mean? Should be named something like TargetScale but config compatibility /shrug
    // ReSharper disable once InconsistentNaming
    public float bScale = 1.4f;
    public float FocusScale = 1.25f;
    public bool IncludeMainTarget = true;
    public bool IncludeFocusTarget = true;

    public void Init(BigPlayerDebuffs ownPlugin, DalamudPluginInterface dalamudPluginInterface) {
        plugin = ownPlugin;
        pluginInterface = dalamudPluginInterface;
    }

    private void Save() {
        pluginInterface.SavePluginConfig(this);
    }

    public bool DrawConfigUi() {
        var drawConfig = true;

        var scale = ImGui.GetIO().FontGlobalScale;

        var modified = false;

        ImGui.SetNextWindowSize(new Vector2(550, 120) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(550, 110), new Vector2(1100, 650) * scale);
        ImGui.Begin($"{plugin.Name} Configuration", ref drawConfig, ImGuiWindowFlags.NoCollapse);
        // Target UI
        ImGui.BeginGroup();
        modified |= ImGui.Checkbox("##Enable scaling in Target UI", ref IncludeMainTarget);
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Enable scaling in target UI");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!IncludeMainTarget);
        modified |= ImGui.SliderFloat("Scale in Target UI", ref bScale, 1.0F, 2.0F, "%.2f");
        ImGui.EndDisabled();
        ImGui.EndGroup();
        // Focus target
        ImGui.BeginGroup();
        modified |= ImGui.Checkbox("##Enable scaling in Focus Target UI", ref IncludeFocusTarget);
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Enable scaling in focus target UI");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!IncludeFocusTarget);
        modified |= ImGui.SliderFloat("Scale in Focus Target UI", ref FocusScale, 1.0F, 2.0F, "%.2f");
        ImGui.EndDisabled();
        ImGui.EndGroup();
        ImGui.Text("Hint: Ctrl+Click a slider to input a number directly");
        ImGui.End();


        if (modified) {
            plugin.ResetTargetStatus();
            Save();
        }

        return drawConfig;
    }
}