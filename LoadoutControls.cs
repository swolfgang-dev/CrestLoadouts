using System;
using System.Linq;
using BepInEx.Configuration;
using InControl;
using UnityEngine;
using WheresWolfgang.SaveScopedConfig;

namespace CrestLoadouts
{
    public sealed partial class Plugin
    {
        private bool runtimeReady;
        private string compatibilityStatus = "Starting";
        private ConfigEntry<KeyboardShortcut> editorShortcut;
        private ConfigEntry<InputControlType> editorControllerButton;
        private ConfigEntry<AttackSource> copyFromCrest;
        private SaveScopedConfigEntry<string> namedPresets;
        private string presetName = string.Empty;
        private string presetMessage = string.Empty;
        private static readonly AttackSlot[] PresetSlots =
            { AttackSlot.Neutral, AttackSlot.Up, AttackSlot.Down, AttackSlot.Dash, AttackSlot.NeedleStrike };

        private void BindLoadoutControls()
        {
            editorShortcut = Config.Bind("Controls", "Open Attack Editor", new KeyboardShortcut(KeyCode.C),
                "Keyboard shortcut while selecting a crest in the inventory.");
            editorControllerButton = Config.Bind("Controls", "Attack Editor Controller Button", InputControlType.Action3,
                "Mapped controller button while selecting a crest. None disables this shortcut.");
            copyFromCrest = Config.Bind("Presets", "Copy From Crest", AttackSource.Hunter,
                "Source crest whose current saved five-slot loadout will be copied to the equipped crest.");
            namedPresets = saveConfig.Bind("Presets", "Named Loadouts", string.Empty,
                new SaveScopedConfigDescription("Stored named loadouts for this save.") { VisibleWhen = () => false });
            ModSettingsUi.BindPanel(Config, "Loadout Manager", DrawLoadoutControls,
                "Save, load, delete, and copy five-slot loadouts. Applying a loadout respects bench and unlock rules.");
        }

        private void ClearPresetFeedback()
        {
            presetName = string.Empty;
            presetMessage = string.Empty;
        }

        internal bool IsEditorShortcutPressed()
        {
            return editorShortcut.Value.IsDown() ||
                (editorControllerButton.Value != InputControlType.None &&
                 InputManager.ActiveDevice.GetControl(editorControllerButton.Value).WasPressed);
        }

        internal bool SaveNamedPreset(string name, string crest)
        {
            if (!runtimeReady || !SaveScopedConfig.HasActiveSave || !IsSupportedCrestId(crest))
            { presetMessage = "Load a save and equip a supported crest first."; return false; }
            string value = string.Join(",", PresetSlots.Select(slot => GetSaveAttackSource(crest, slot).ToString()));
            string updated, error;
            if (!PresetLibrary.TrySave(namedPresets.Value, name, value, out updated, out error))
            { presetMessage = error; return false; }
            namedPresets.Value = updated;
            presetMessage = "Saved " + name.Trim() + " for " + SaveScopedConfig.ActiveSaveKey + ".";
            return true;
        }

        internal bool LoadNamedPreset(string name, string crest)
        {
            string value;
            if (!PresetLibrary.Read(namedPresets.Value).TryGetValue(name, out value))
            { presetMessage = "Preset not found on this save."; return false; }
            return ApplyLoadout(crest, value.Split(','));
        }

        internal bool CopyLoadout(string sourceCrest, string targetCrest)
        {
            if (!IsSupportedCrestId(sourceCrest))
            { presetMessage = "Unsupported source crest."; return false; }
            return ApplyLoadout(targetCrest, PresetSlots.Select(slot => GetSaveAttackSource(sourceCrest, slot).ToString()).ToArray());
        }

        private bool ApplyLoadout(string crest, string[] values)
        {
            if (!runtimeReady || !SaveScopedConfig.HasActiveSave || !IsSupportedCrestId(crest))
            { presetMessage = "Load a save and equip a supported crest first."; return false; }
            if (!CanChangeAttackSources())
            { presetMessage = AttackBenchMessage; return false; }
            var sources = new AttackSource[PresetSlots.Length];
            var available = GetAvailableAttackSources(crest);
            if (values.Length != sources.Length)
            { presetMessage = "Invalid preset: expected five attacks."; return false; }
            for (int i = 0; i < values.Length; i++)
            {
                if (!Enum.TryParse(values[i], out sources[i]) || !Enum.IsDefined(typeof(AttackSource), sources[i]) ||
                    !available.Contains(sources[i]))
                { presetMessage = "Preset contains an invalid or locked attack source. Nothing was changed."; return false; }
            }
            for (int i = 0; i < sources.Length; i++)
                GetSaveAttackSourceEntry(crest, PresetSlots[i]).Value = sources[i];
            if (NormalizeCrestId(crest) == NormalizeCrestId(GetEquippedCrestId()))
                AttackOverrideController.Apply(HeroController.UnsafeInstance);
            presetMessage = "Applied five attacks to " + GetCrestDisplayName(crest) + ".";
            return true;
        }

        private void DrawLoadoutControls()
        {
            ModSettingsUi.Label(compatibilityStatus);
            if (!SaveScopedConfig.HasActiveSave) { ModSettingsUi.Label("Load a save to manage presets."); return; }
            string crest = GetEquippedCrestId();
            ModSettingsUi.Label(SaveScopedConfig.ActiveSaveKey + " · " + GetCrestDisplayName(crest));
            presetName = GUILayout.TextField(presetName, 80);
            if (ModSettingsUi.Button("Save current attacks as named preset", runtimeReady)) SaveNamedPreset(presetName, crest);
            foreach (var preset in PresetLibrary.Read(namedPresets.Value))
            {
                ModSettingsUi.Label(preset.Key);
                GUILayout.BeginHorizontal();
                if (ModSettingsUi.Button("Load preset", runtimeReady)) LoadNamedPreset(preset.Key, crest);
                if (ModSettingsUi.Button("Delete preset"))
                    namedPresets.Value = PresetLibrary.Delete(namedPresets.Value, preset.Key);
                GUILayout.EndHorizontal();
            }
            if (ModSettingsUi.Button("Copy attacks from " + copyFromCrest.Value, runtimeReady))
                CopyLoadout(GetCanonicalCrestId(copyFromCrest.Value), crest);
            if (ModSettingsUi.Button("Reset this crest to its default attacks", runtimeReady && CanChangeAttackSources()))
            { ResetSaveAttackSources(crest); presetMessage = "Restored this crest's default attacks."; }
            if (!string.IsNullOrEmpty(presetMessage)) ModSettingsUi.Label(presetMessage);
        }
    }
}
