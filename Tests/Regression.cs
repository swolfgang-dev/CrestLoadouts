using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using GlobalEnums;
using HarmonyLib;
using InControl;
using UnityEngine;
using WheresWolfgang.SaveScopedConfig;

[BepInPlugin("local.whereswolfgang.regression", "Isolated Mod Regression", "1.0.0")]
[BepInDependency(SaveScopedConfigPlugin.PluginGuid, "0.4.0")]
[BepInDependency("whereswolfgang.crestloadouts", "0.2.0")]
public sealed class Regression : BaseUnityPlugin
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Regression active;
    private string root;
    private int checks, attacks;
    private HeroController hero;
    private PlayMakerFSM sprint, needle;
    private HeroAnimationController animation;
    private TestPad pad;
    private object crest, tools, abilities, map;
    private Type overrideType, sourceType, slotType;
    private Trace trace;
    private bool checkingUi, capturingDash;
    private readonly List<string> uiErrors = new List<string>();
    private readonly string[] names = { "Hunter", "Reaper", "Wanderer", "Beast", "Witch", "Architect", "Shaman" };
    private readonly string[] crestIds = { "Hunter", "Reaper", "Wanderer", "Warrior", "Witch", "Toolmaster", "Spell", "Hunter_v2", "Hunter_v3" };
    private readonly string[] slots = { "Neutral", "Up", "Down", "Dash", "NeedleStrike" };

    private void Awake()
    {
        root = Environment.GetEnvironmentVariable("SILKSONG_MOD_TEST_ROOT");
        if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root) ||
            !Path.GetFullPath(Application.persistentDataPath).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            Logger.LogError("Test plugin refused to run outside a disposable profile.");
            enabled = false;
            return;
        }
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (checkingUi && (type == LogType.Exception || message.Contains("GUILayout") || message.Contains("GUI Error") || message.Contains("custom drawer"))) uiErrors.Add(message);
        };
        active = this;
        new Harmony("local.whereswolfgang.regression.input").Patch(AccessTools.Method(typeof(HeroController), "DoAttack"),
            prefix: new HarmonyMethod(typeof(Regression), nameof(BlockBufferedNormalAttack)));
        Application.runInBackground = true;
        InputManager.SuspendInBackground = false;
        StartCoroutine(Safely(Run()));
    }

    private IEnumerator Safely(IEnumerator run)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(run);
        while (stack.Count > 0)
        {
            object next = null;
            Exception failure = null;
            try
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                next = current.Current;
                if (next is IEnumerator nested) { stack.Push(nested); continue; }
            }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                Logger.LogError("REGRESSION FAILED " + failure);
                File.WriteAllText(Path.Combine(root, "result.txt"), "FAIL after " + checks + " checks: " + failure);
                yield break;
            }
            yield return next;
        }
    }

    private static object Field(object instance, string name)
    { return (instance as Type ?? instance.GetType()).GetField(name, Flags).GetValue(instance is Type ? null : instance); }
    private static object Call(object instance, string name, params object[] args)
    {
        return (instance as Type ?? instance.GetType()).GetMethods(Flags)
            .First(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(instance is Type ? null : instance, args);
    }
    private static object Plugin(string name) => Chainloader.PluginInfos["whereswolfgang." + name].Instance;
    private static ConfigFile PluginConfig(object instance) => ((BaseUnityPlugin)instance).Config;
    private void Set(object instance, string category, string key, object value)
    {
        ConfigEntryBase entry = PluginConfig(instance)[new ConfigDefinition(category, key)];
        entry.BoxedValue = entry.SettingType.IsEnum && value is string text ? Enum.Parse(entry.SettingType, text) : value;
    }
    // Sprint input must remain held for chained attacks, but its queued neutral
    // follow-up is a different slot and must not contaminate the dash trace.
    private static bool BlockBufferedNormalAttack() => active == null || !active.capturingDash;
    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Logger.LogInfo("TEST PASS " + message);
    }
    private IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 60;
        while (!Resources.FindObjectsOfTypeAll<GameManager>().Any())
        {
            foreach (var selector in Resources.FindObjectsOfTypeAll<LanguageSelector>())
                if (selector.gameObject.activeInHierarchy) { selector.SetLanguage("EN"); selector.ConfirmLanguage(); }
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Game manager did not initialize");
            yield return new WaitForSecondsRealtime(.2f);
        }
        yield return new WaitForSecondsRealtime(5);
        GameManager.instance.profileID = 1;
        GameManager.instance.StartNewGame(false, false);
        deadline = Time.realtimeSinceStartup + 120;
        while (HeroController.UnsafeInstance == null || GameManager.instance.GameState != GameState.PLAYING)
        {
            foreach (var opening in Resources.FindObjectsOfTypeAll<OpeningSequence>())
                if (opening.gameObject.activeInHierarchy) opening.StartCoroutine(opening.Skip());
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Disposable game did not reach gameplay");
            yield return new WaitForSecondsRealtime(1);
        }
        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo
        { SceneName = "Bone_01", EntryGateName = "left1", PreventCameraFadeOut = true });
        yield return new WaitForSecondsRealtime(8);
        deadline = Time.realtimeSinceStartup + 60;
        while (GameManager.instance.sceneName != "Bone_01" || GameManager.instance.IsInSceneTransition)
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Test arena did not load");
            yield return new WaitForSecondsRealtime(1);
        }
        hero = HeroController.instance;
        GameManager.instance.playerData.isInvincible = true;
        hero.SetDamageMode(2);
        Logger.LogInfo("Test arena " + GameManager.instance.sceneName + " initial position " + hero.transform.position);
        hero.RegainControl();
        hero.acceptingInput = true;
        crest = Plugin("crestloadouts"); tools = Plugin("equipalltools");
        abilities = Plugin("abilitytoggles"); map = Plugin("automap");
        foreach (var plugin in new[] { crest, tools, abilities, map, Plugin("savescopedconfig") }) PluginConfig(plugin).SaveOnConfigSet = false;
        overrideType = crest.GetType().Assembly.GetType("CrestLoadouts.AttackOverrideController");
        sourceType = crest.GetType().Assembly.GetType("CrestLoadouts.AttackSource");
        slotType = crest.GetType().Assembly.GetType("CrestLoadouts.AttackSlot");
        sprint = hero.GetComponents<PlayMakerFSM>().First(fsm => fsm.FsmName == "Sprint");
        needle = hero.GetComponents<PlayMakerFSM>().First(fsm => fsm.FsmName == "Nail Arts");
        animation = hero.GetComponent<HeroAnimationController>();
        pad = new TestPad(); InputManager.AttachDevice(pad);
        yield return new WaitForSecondsRealtime(1);
        var input = (InputHandler)Field(hero, "inputHandler");
        input.inputActions.Attack.ClearBindings();
        input.inputActions.Attack.AddBinding(new DeviceBindingSource(InputControlType.Action3));
        Check(Application.persistentDataPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal), "save directory is isolated");
        Check(SaveScopedConfig.ActiveSaveSlot == 1, "new game activated its configuration slot");
        Check((bool)Field(crest, "runtimeReady") && (bool)Field(tools, "runtimeReady"), "consumer compatibility checks passed");
        TestEventsAndCompatibility();
        TestPresetsAndStatus();
        TestSaveReset();
        yield return TestInputs();
        Prepare("Hunter");
        yield return TestCloakInGame();
        yield return new WaitForSecondsRealtime(.3f);
        yield return TestArchitectDashRouting();
        if (Environment.GetEnvironmentVariable("SILKSONG_MOD_TEST_FEATURES_ONLY") != "1") yield return TestMixedAttacks();
        // Search expands matching panels, so each custom drawer actually renders.
        if (Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.configurationmanager", out var manager))
        {
            var managerType = manager.Instance.GetType();
            checkingUi = true;
            managerType.GetProperty("DisplayingWindow", Flags).SetValue(manager.Instance, true);
            string[] queries = { "Loadout Manager", "Tool Presets and Effective Settings", "Effective Abilities", "Mapping Status", "Runtime" };
            for (int panel = 0; panel < queries.Length; panel++)
            {
                managerType.GetProperty("SearchString", Flags).SetValue(manager.Instance, queries[panel]);
                yield return new WaitForSecondsRealtime(.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(root, "configuration-" + panel + ".png"));
                yield return new WaitForSecondsRealtime(.3f);
            }
            checkingUi = false;
            Check(uiErrors.Count == 0, "all five Configuration Manager panels render without GUI exceptions: " + string.Join(";", uiErrors));
        }
        string result = "PASS " + checks + " checks; " + attacks + " live attack executions";
        Logger.LogInfo(result);
        File.WriteAllText(Path.Combine(root, "result.txt"), result);
    }

    private void TestEventsAndCompatibility()
    {
        int notified = 0;
        Action broken = () => throw new InvalidOperationException("Intentional test subscriber failure");
        Action healthy = () => notified++;
        SaveScopedConfig.ActiveSaveChanged += broken;
        SaveScopedConfig.ActiveSaveChanged += healthy;
        try { Call(typeof(SaveScopedConfig), "SetActiveSave", 2, PermadeathModes.Off); }
        finally
        {
            SaveScopedConfig.ActiveSaveChanged -= broken;
            SaveScopedConfig.ActiveSaveChanged -= healthy;
            Call(typeof(SaveScopedConfig), "SetActiveSave", 1, PermadeathModes.Off);
        }
        Check(notified == 1, "a broken save listener does not block a healthy listener in game");
        string status;
        var owner = new Harmony("local.regression.compatibility");
        Check(!ModCompatibility.TryPatch(owner, typeof(Regression).Assembly, Logger, out status, new MemberInfo[] { null }), "missing APIs disable hooks cleanly");
        Check(!ModCompatibility.TryPatch(owner, typeof(Regression).Assembly, Logger, out status), "a bad patch reports unavailable instead of escaping startup");
        Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method).Owners.Contains(owner.Id)), "failed compatibility installation leaves no partial hooks");
        Check(ModSettingsUi.ExplainEnablement(false, true, false).Contains("global"), "global source is explained");
        Check(ModSettingsUi.ExplainEnablement(true, false, false).Contains("normally"), "native acquisition is explained");
    }

    private string[] ReadSources(string id) => slots.Select(slot => Call(crest, "GetSaveAttackSource", id, Enum.Parse(slotType, slot)).ToString()).ToArray();
    private void WriteSources(string id, int[] values)
    {
        for (int slot = 0; slot < slots.Length; slot++)
        {
            object entry = Call(crest, "GetSaveAttackSourceEntry", id, Enum.Parse(slotType, slots[slot]));
            entry.GetType().GetProperty("Value").SetValue(entry, Enum.Parse(sourceType, names[values[slot]]));
        }
        Call(overrideType, "Apply", hero);
    }
    private void TestPresetsAndStatus()
    {
        Set(crest, "Global: Attack Sources", "Allow Locked Crest Attacks", true);
        Set(crest, "Global: Attack Sources", "Require Bench To Change Attack", false);
        ToolItemManager.SetEquippedCrest("Hunter"); hero.ResetAllCrestState();
        WriteSources("Hunter", new[] { 0, 1, 2, 3, 4 });
        var expected = ReadSources("Hunter");
        Check((bool)Call(crest, "SaveNamedPreset", "Mixed / 雪", "Hunter"), "named preset captures all five slots");
        PluginConfig(crest).Save(); PluginConfig(crest).Reload();
        WriteSources("Hunter", new[] { 6, 6, 6, 6, 6 });
        Check((bool)Call(crest, "LoadNamedPreset", "mixed / 雪", "Hunter"), "preset survives configuration serialization and loads ignoring case");
        Check(ReadSources("Hunter").SequenceEqual(expected), "loaded preset restores every slot");
        Check((bool)Call(crest, "CopyLoadout", "Hunter", "Reaper") && ReadSources("Reaper").SequenceEqual(expected), "copying across crests preserves all five selections");
        Call(typeof(SaveScopedConfig), "SetActiveSave", 2, PermadeathModes.Off);
        Check(!(bool)Call(crest, "LoadNamedPreset", "Mixed / 雪", "Hunter"), "named presets are isolated by save slot");
        Call(typeof(SaveScopedConfig), "SetActiveSave", 1, PermadeathModes.Off);
        Set(crest, "Global: Attack Sources", "Require Bench To Change Attack", true);
        GameManager.instance.playerData.atBench = false;
        Check(!(bool)Call(crest, "LoadNamedPreset", "Mixed / 雪", "Hunter"), "preset application obeys bench requirement");
        Check(ReadSources("Hunter").SequenceEqual(expected), "bench refusal makes no partial changes");
        Set(crest, "Global: Attack Sources", "Require Bench To Change Attack", false);
        Set(crest, "Global: Attack Sources", "Allow Locked Crest Attacks", false);
        Check(!(bool)Call(crest, "LoadNamedPreset", "Mixed / 雪", "Hunter"), "locked sources are rejected atomically");
        Check(ReadSources("Hunter").SequenceEqual(expected), "unlock refusal preserves the existing loadout");
        Set(crest, "Global: Attack Sources", "Allow Locked Crest Attacks", true);
        Set(tools, "Global: General", "Bench Requirement", false);
        Set(tools, "Save 1: General", "Bench Requirement", true);
        Check(!(bool)Call(tools, "ApplyToolPreset", "All"), "tool presets obey bench requirement");
        Set(tools, "Save 1: General", "Bench Requirement", false);
        var definitions = (IEnumerable)Field(tools.GetType(), "Tools");
        foreach (string preset in new[] { "Defense", "Exploration", "All", "Clear" })
        {
            foreach (object definition in definitions)
            {
                object entry = Field(definition, "SaveEnabled");
                entry.GetType().GetProperty("Value").SetValue(entry, true);
            }
            Check((bool)Call(tools, "ApplyToolPreset", preset), "tool preset applies: " + preset);
            foreach (object definition in definitions)
            {
                bool available = (bool)definition.GetType().GetProperty("IsAvailableForActiveSave").GetValue(definition);
                string category = (string)Field(definition, "Category");
                bool expectedValue = available && (preset == "All" || (preset == "Defense" && category.Contains("Defense")) ||
                    (preset == "Exploration" && category.Contains("Exploration")));
                Check((bool)Field(definition, "SaveEnabled").GetType().GetProperty("Value").GetValue(Field(definition, "SaveEnabled")) == expectedValue,
                    "tool preset membership " + preset + "/" + Field(definition, "ConfigKey"));
            }
        }
        var first = definitions.Cast<object>().First();
        var global = (ConfigEntry<bool>)Field(first, "GlobalEnabled");
        global.Value = true;
        Call(tools, "ApplyToolPreset", "Clear");
        Check(global.Value && ((string)Call(tools, "GetEffectiveToolStatus")).Contains("global"), "clearing save tool toggles preserves and explains global settings");
        global.Value = false;
        int nativeSilkRegen = GameManager.instance.playerData.silkRegenMax;
        GameManager.instance.playerData.silkRegenMax = GameManager.instance.playerData.CurrentSilkMax;
        Check(((string)Call(abilities, "GetAbilityStatus")).Contains("Sylphsong: Available normally"), "ability status recognizes native silk regeneration");
        GameManager.instance.playerData.silkRegenMax = nativeSilkRegen;
        Set(abilities, "Global: Ancestral Arts", "Swift Step", true);
        Check(((string)Call(abilities, "GetAbilityStatus")).Contains("global"), "ability panel explains global enablement");
        Check(((string)Call(abilities, "GetAbilityStatus")).Contains("waiting for the normal fast-travel unlock") == false,
            "disabled teleport ability does not claim it is enabled");
        Set(abilities, "Global: Needolin Memories", "Beastling Call", true);
        Check(((string)Call(abilities, "GetAbilityStatus")).Contains("waiting for the normal fast-travel unlock"), "teleport prerequisites are explained");
        Set(abilities, "Global: Needolin Memories", "Beastling Call", false);
        Set(map, "Global: General", "Quill Required", false);
        Set(map, "Save 1: General", "Quill Required", true);
        GameManager.instance.playerData.hasQuill = false;
        Check(((string)Call(map, "GetMappingStatus")).Contains("Waiting for a usable quill"), "map status explains a missing quill");
        foreach (var plugin in new[] { crest, tools, abilities, map })
            Check(PluginConfig(plugin).Any(entry => entry.Value.Description.Tags.Any(tag => tag.GetType().GetField("CustomDrawer") != null)), "configuration panel is registered for " + plugin.GetType().Namespace);
    }

    private void TestSaveReset()
    {
        object helper = Plugin("savescopedconfig");
        Call(typeof(SaveScopedConfig), "SetActiveSave", 3, PermadeathModes.Off);
        WriteSources("Hunter", new[] { 1, 2, 3, 4, 5 });
        Check((bool)Call(crest, "SaveNamedPreset", "Reset test", "Hunter"), "third slot can store a named preset");
        Set(helper, "Settings", "Reset Saved Config On Delete Save", false);
        Call(helper.GetType(), "ClearSaveConfigForSlot", 3);
        Check((bool)Call(crest, "LoadNamedPreset", "Reset test", "Hunter"), "disabled reset preserves the slot's named presets");
        int notified = 0;
        Action<int> broken = slot => throw new InvalidOperationException("Intentional reset listener failure");
        Action<int> healthy = slot => { if (slot == 3) notified++; };
        SaveScopedConfig.SaveSlotResetToDefaults += broken;
        SaveScopedConfig.SaveSlotResetToDefaults += healthy;
        Set(helper, "Settings", "Reset Saved Config On Delete Save", true);
        try { Call(helper.GetType(), "ClearSaveConfigForSlot", 3); }
        finally
        {
            SaveScopedConfig.SaveSlotResetToDefaults -= broken;
            SaveScopedConfig.SaveSlotResetToDefaults -= healthy;
            Set(helper, "Settings", "Reset Saved Config On Delete Save", false);
        }
        Check(notified == 1, "broken reset listener does not block the healthy listener in game");
        Check(!(bool)Call(crest, "LoadNamedPreset", "Reset test", "Hunter"), "enabled reset removes the cleared slot's named presets");
        Check(ReadSources("Hunter").All(source => source == "Hunter"), "enabled reset restores every attack slot to its default");
        Check(SaveScopedConfig.ActiveSaveSlot == 3, "reset retains the active slot identity");
        Call(typeof(SaveScopedConfig), "SetActiveSave", 1, PermadeathModes.Off);
        Check((bool)Call(crest, "LoadNamedPreset", "Mixed / 雪", "Hunter"), "clearing slot three preserves slot one's presets");
    }

    private IEnumerator TestInputs()
    {
        Set(tools, "Controls", "Tool Toggle Keyboard Modifier", KeyCode.None);
        Set(tools, "Controls", "Tool Toggle Controller Modifier", InputControlType.RightBumper);
        pad.Held.Add(InputControlType.RightBumper); yield return null;
        Check((bool)Call(tools.GetType(), "IsInventoryToggleModifierHeld"), "tool modifier follows custom controller binding");
        pad.Held.Clear(); yield return null;
        Check(!(bool)Call(tools.GetType(), "IsInventoryToggleModifierHeld"), "releasing the custom modifier stops tool toggling");
        Set(crest, "Controls", "Open Attack Editor", new KeyboardShortcut(KeyCode.None));
        Set(crest, "Controls", "Attack Editor Controller Button", InputControlType.Action4);
        pad.Held.Add(InputControlType.Action4); yield return null;
        Check((bool)Call(crest, "IsEditorShortcutPressed"), "attack editor follows custom controller binding");
        pad.Held.Clear(); yield return null;
        Set(crest, "Controls", "Attack Editor Controller Button", InputControlType.Action3);
        pad.Held.Add(InputControlType.Action4); yield return null;
        Check(!(bool)Call(crest, "IsEditorShortcutPressed"), "old controller shortcut does not trigger after rebinding");
        pad.Held.Clear(); yield return null;
    }

    private void Prepare(string id)
    {
        pad.Held.Clear(); hero.CancelAttack(); sprint.SetState("Idle");
        needle.SetState("Cancel All"); needle.SetState("Inactive");
        hero.RegainControl(); hero.acceptingInput = true;
        hero.cState.isSprinting = false; hero.cState.wallSliding = false; hero.cState.wallScrambling = false;
        hero.cState.onGround = true; hero.cState.facingRight = true; hero.cState.altAttack = false;
        hero.transform.position = new Vector3(50.4f, 4.57f, 0);
        hero.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        hero.AffectedByGravity(true);
        ToolItemManager.SetEquippedCrest(id); hero.ResetAllCrestState();
        hero.cState.altAttack = false;
        AccessTools.Field(typeof(HeroController), "altAttackTime").SetValue(hero, float.NegativeInfinity);
        animation.StartControl(); animation.StartControlToIdle(true);
    }
    private sealed class Trace
    {
        internal readonly HashSet<tk2dSpriteAnimationClip> Clips = new HashSet<tk2dSpriteAnimationClip>();
        internal readonly HashSet<string> States = new HashSet<string>();
        internal readonly HashSet<string> AllClips = new HashSet<string>();
        internal readonly HashSet<string> Hits = new HashSet<string>();
    }
    private string ObjectPath(Transform item)
    {
        string path = item.name;
        while (item.parent && item.parent != hero.transform) { item = item.parent; path = item.name + "/" + path; }
        return path;
    }
    private IEnumerator Attack(int slot)
    {
        trace = new Trace();
        capturingDash = slot == 3;
        if (slot < 3)
        {
            if (slot == 2) { hero.cState.onGround = false; hero.transform.position += Vector3.up * 6; }
            Call(hero, "Attack", slot == 0 ? AttackDirection.normal : slot == 1 ? AttackDirection.upward : AttackDirection.downward);
        }
        else if (slot == 3)
        {
            float groundDeadline = Time.realtimeSinceStartup + 2;
            yield return new WaitForFixedUpdate();
            while (!hero.CheckTouchingGround(true) && Time.realtimeSinceStartup < groundDeadline) yield return new WaitForFixedUpdate();
            if (!hero.CheckTouchingGround(true)) throw new InvalidOperationException("Dash test could not reach the ground.");
            hero.cState.onGround = true;
            hero.RelinquishControl(); animation.StopControl(); pad.Held.Add(InputControlType.Action3);
            yield return null;
            hero.cState.isSprinting = true; sprint.SetState("Dash Stab Dir");
        }
        else { needle.SendEvent("DO ATTACK"); if (needle.ActiveStateName == "Inactive") needle.SetState("Get Charge Slash"); }
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < 2.1f)
        {
            if (Time.realtimeSinceStartup - start > .5f) pad.Held.Clear();
            trace.States.Add(sprint.ActiveStateName);
            var clip = animation.animator.CurrentClip;
            if (clip != null) trace.AllClips.Add(clip.name);
            if (clip != null && new[] { "Slash", "Spike", "Stab", "Dash", "Charge", "Drill", "Attack", "Lash", "Spin", "Down", "Needle" }
                .Any(word => clip.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) &&
                !new[] { "Recover", "Land", "To Run" }.Any(word => clip.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)) trace.Clips.Add(clip);
            foreach (var damager in hero.GetComponentsInChildren<DamageEnemies>())
                if (damager.gameObject.activeInHierarchy && damager.GetComponents<Collider2D>().Any(collider => collider.enabled)) trace.Hits.Add(ObjectPath(damager.transform));
            bool recovered = slot < 2 ? !hero.cState.attacking : slot == 2 ?
                !hero.cState.attacking && !hero.cState.downSpikeAntic && !hero.cState.downSpiking && !hero.cState.downAttacking && !hero.cState.downSpikeRecovery :
                slot == 3 ? sprint.ActiveStateName == "Idle" : needle.ActiveStateName == "Inactive";
            if (recovered && Time.realtimeSinceStartup - start > .85f) break;
            yield return null;
        }
        capturingDash = false;
        attacks++;
    }
    private IEnumerator TestArchitectDashRouting()
    {
        Prepare("Toolmaster"); WriteSources("Toolmaster", Enumerable.Repeat(5, 5).ToArray());
        yield return Attack(3); var expected = trace;
        Check(expected.Hits.Count > 0 && expected.Clips.Count > 0, "native Architect drill reference executes from grounded sprint: clips=" + string.Join(",", expected.AllClips) + " hits=" + string.Join(",", expected.Hits) + " states=" + string.Join(",", expected.States));
        Prepare("Hunter"); WriteSources("Hunter", new[] { 2, 3, 4, 5, 6 });
        yield return Attack(3);
        Check(trace.Hits.SetEquals(expected.Hits), "Architect dash uses its own source collider in a mixed loadout: " + string.Join(",", trace.Hits));
        Check(trace.Clips.IsSupersetOf(expected.Clips), "Architect dash uses its own source animation in a mixed loadout");
        Prepare("Wanderer"); WriteSources("Wanderer", Enumerable.Repeat(2, 5).ToArray());
        yield return Attack(0); expected = trace;
        Prepare("Hunter"); WriteSources("Hunter", new[] { 2, 3, 4, 5, 6 });
        yield return Attack(0);
        Check(trace.Hits.SetEquals(expected.Hits), "neutral attack keeps its own source after Architect dash");
    }

    private readonly List<string> attackFailures = new List<string>();
    private void CheckAttack(bool condition, string message)
    {
        if (condition) { checks++; Logger.LogInfo("TEST PASS " + message); }
        else { attackFailures.Add(message); Logger.LogError("ATTACK COMPARISON FAILED " + message); }
    }
    private IEnumerator TestMixedAttacks()
    {
        var baseline = new Trace[7, 5];
        for (int source = 0; source < 7; source++)
            for (int slot = 0; slot < 5; slot++)
            {
                Prepare(crestIds[source]); WriteSources(crestIds[source], Enumerable.Repeat(source, 5).ToArray());
                yield return Attack(slot); baseline[source, slot] = trace;
                Check(trace.Clips.Count > 0 && trace.Hits.Count > 0, "native reference " + names[source] + "/" + slots[slot] + " clips=" + string.Join(",", trace.AllClips) + " hits=" + string.Join(",", trace.Hits));
            }
        bool full = Environment.GetEnvironmentVariable("SILKSONG_MOD_TEST_FULL") == "1";
        foreach (string id in crestIds)
        {
            // GF(7) rows (a + b*slot) cover every source pair for every pair of slots.
            // The default rotation subset is a smaller real-attack regression suite.
            int rows = full ? 49 : 7;
            for (int row = 0; row < rows; row++)
            {
                int a = full ? row / 7 : row, b = full ? row % 7 : 1;
                var selection = Enumerable.Range(0, 5).Select(slot => (a + b * slot) % 7).ToArray();
                for (int slot = 0; slot < 5; slot++)
                {
                    Prepare(id); WriteSources(id, selection);
                    yield return Attack(slot);
                    Trace expected = baseline[selection[slot], slot];
                    string label = id + " mix " + row + "/" + slots[slot];
                    CheckAttack(trace.Clips.IsSupersetOf(expected.Clips), "source animations " + label +
                        " expected=" + string.Join(",", expected.Clips.Select(clip => clip.name)) + " actual=" + string.Join(",", trace.AllClips));
                    CheckAttack(trace.Hits.SetEquals(expected.Hits), "source damage colliders " + label +
                        " expected=" + string.Join(",", expected.Hits) + " actual=" + string.Join(",", trace.Hits));
                    CheckAttack(GameManager.instance.playerData.CurrentCrestID == id, "equipped crest preserved " + label);
                }
            }
            Logger.LogInfo("MIXED CREST COMPLETE " + id);
        }
        Prepare("Hunter");
        if (attackFailures.Count > 0) throw new InvalidOperationException(attackFailures.Count + " attack comparisons failed: " + string.Join("; ", attackFailures.Take(10)));
    }

    private IEnumerator TestCloakInGame()
    {
        var brolly = abilities.GetType().Assembly.GetType("AbilityToggles.BrollyAbility");
        Set(abilities, "Controls", "Cloak Extra Game Action", "None");
        Set(abilities, "Controls", "Cloak Extra Keyboard Key", KeyCode.None);
        Set(abilities, "Controls", "Cloak Extra Controller Button", InputControlType.RightBumper);
        Set(abilities, "Global: Other Abilities", "Drifters Cloak", true);
        bool acquired = GameManager.instance.playerData.hasBrolly;
        pad.Held.Add(InputControlType.RightBumper); yield return null;
        Check((bool)Call(brolly, "IsExtraPressed", (object)null), "custom cloak controller control is held");
        pad.Held.Clear(); yield return null;
        Check(!(bool)Call(brolly, "IsExtraPressed", (object)null), "custom cloak control releases");
        hero.RegainControl(); hero.acceptingInput = true;
        hero.transform.position += Vector3.up * 6; hero.cState.onGround = false; hero.cState.falling = true;
        hero.GetComponent<Rigidbody2D>().linearVelocity = Vector2.down;
        for (int frame = 0; frame < 6; frame++) yield return new WaitForFixedUpdate();
        Check((bool)Call(hero, "CanFloat", true), "virtual cloak meets native floating prerequisites");
        pad.Held.Add(InputControlType.RightBumper);
        float floatDeadline = Time.realtimeSinceStartup + 2;
        while (hero.umbrellaFSM.ActiveStateName != "Float Idle" && Time.realtimeSinceStartup < floatDeadline) yield return null;
        Check(hero.umbrellaFSM.ActiveStateName == "Float Idle" && hero.umbrellaFSM.FsmVariables.FindFsmBool("Is Active").Value,
            "custom controller binding starts native Drifter's Cloak: " + hero.umbrellaFSM.ActiveStateName);
        yield return new WaitForSecondsRealtime(.15f);
        Check(hero.umbrellaFSM.ActiveStateName == "Float Idle", "holding the custom control sustains native floating");
        Check(GameManager.instance.playerData.hasBrolly == acquired, "custom cloak control preserves native progression");
        pad.Held.Clear();
        floatDeadline = Time.realtimeSinceStartup + 2;
        while (hero.umbrellaFSM.FsmVariables.FindFsmBool("Is Active").Value && Time.realtimeSinceStartup < floatDeadline) yield return null;
        Check(!hero.umbrellaFSM.FsmVariables.FindFsmBool("Is Active").Value, "releasing the custom control ends native floating");
    }

    private sealed class TestPad : InputDevice
    {
        internal readonly HashSet<InputControlType> Held = new HashSet<InputControlType>();
        internal TestPad() : base("Silksong Regression Controller")
        {
            foreach (var control in new[] { InputControlType.Action3, InputControlType.Action4, InputControlType.RightBumper, InputControlType.LeftStickButton }) AddControl(control, control.ToString());
        }
        public override void Update(ulong tick, float deltaTime)
        {
            foreach (var control in new[] { InputControlType.Action3, InputControlType.Action4, InputControlType.RightBumper, InputControlType.LeftStickButton }) UpdateWithState(control, Held.Contains(control), tick, deltaTime);
        }
    }
}

// Deliberately invalid fixture: only the explicit rollback test patches this assembly.
[HarmonyPatch(typeof(GameManager), "RegressionMethodThatDoesNotExist")]
internal static class MissingPatchFixture { private static void Prefix() { } }
