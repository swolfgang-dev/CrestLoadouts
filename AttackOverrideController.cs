using GlobalEnums;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CrestLoadouts
{
    internal static class AttackOverrideController
    {
        private static readonly FieldInfo ConfigsField = AccessTools.Field(typeof(HeroController), "configs");
        private static readonly FieldInfo CrestConfigField = AccessTools.Field(typeof(HeroController), "crestConfig");
        private static readonly FieldInfo CurrentConfigGroupField = AccessTools.Field(typeof(HeroController), "<CurrentConfigGroup>k__BackingField");
        private static readonly FieldInfo AnimationHeroControllerField = AccessTools.Field(typeof(HeroAnimationController), "heroCtrl");

        private static readonly FieldInfo NormalSlashField = AccessTools.Field(typeof(HeroController), "normalSlash");
        private static readonly FieldInfo NormalSlashDamagerField = AccessTools.Field(typeof(HeroController), "normalSlashDamager");
        private static readonly FieldInfo AlternateSlashField = AccessTools.Field(typeof(HeroController), "alternateSlash");
        private static readonly FieldInfo AlternateSlashDamagerField = AccessTools.Field(typeof(HeroController), "alternateSlashDamager");
        private static readonly FieldInfo UpSlashField = AccessTools.Field(typeof(HeroController), "upSlash");
        private static readonly FieldInfo UpSlashDamagerField = AccessTools.Field(typeof(HeroController), "upSlashDamager");
        private static readonly FieldInfo AltUpSlashField = AccessTools.Field(typeof(HeroController), "altUpSlash");
        private static readonly FieldInfo AltUpSlashDamagerField = AccessTools.Field(typeof(HeroController), "altUpSlashDamager");
        private static readonly FieldInfo DownSpikeField = AccessTools.Field(typeof(HeroController), "downSpike");
        private static readonly FieldInfo DownSlashField = AccessTools.Field(typeof(HeroController), "downSlash");
        private static readonly FieldInfo DownSlashDamagerField = AccessTools.Field(typeof(HeroController), "downSlashDamager");
        private static readonly FieldInfo AltDownSpikeField = AccessTools.Field(typeof(HeroController), "altDownSpike");
        private static readonly FieldInfo AltDownSlashField = AccessTools.Field(typeof(HeroController), "altDownSlash");
        private static readonly FieldInfo AltDownSlashDamagerField = AccessTools.Field(typeof(HeroController), "altDownSlashDamager");
        private static readonly FieldInfo DashStabField = AccessTools.Field(typeof(HeroController.ConfigGroup), "DashStab");
        private static readonly FieldInfo DashStabAltField = AccessTools.Field(typeof(HeroController.ConfigGroup), "DashStabAlt");
        private static readonly FieldInfo ChargeSlashField = AccessTools.Field(typeof(HeroController.ConfigGroup), "ChargeSlash");

        // Dash/needle overrides change ConfigGroup references and ScriptableObject fields.
        // Restore their vanilla values before resolving the next source, including resets.
        private static readonly List<OriginalGroupState> OriginalGroups = new List<OriginalGroupState>();
        private static HeroController originalGroupsHero;
        private static HutongGames.PlayMaker.FsmBool sprintInAttack;
        private static PlayMakerFSM nailArtsFsm;
        private static readonly List<HutongGames.PlayMaker.Actions.GetHeroAttackObject> CachedDashObjects = new List<HutongGames.PlayMaker.Actions.GetHeroAttackObject>();
        private static readonly List<HutongGames.PlayMaker.Actions.GetHeroConfigVariable> CachedDashVariables = new List<HutongGames.PlayMaker.Actions.GetHeroConfigVariable>();
        private static readonly string[] OverriddenConfigFields =
        {
            "dashStabSpeed", "dashStabTime", "forceShortDashStabBounce",
            "dashStabBounceJumpSpeed", "dashStabSteps", "canHarpoonDash",
            "chargeSlashRecoils", "chargeSlashLungeSpeed", "chargeSlashLungeDeceleration", "chargeSlashChain",
        };

        private static void RestoreOriginalGroups()
        {
            foreach (OriginalGroupState state in OriginalGroups)
            {
                state.Group.DashStab = state.DashStab;
                state.Group.DashStabAlt = state.DashStabAlt;
                state.Group.ChargeSlash = state.ChargeSlash;
                if (state.Config != null)
                {
                    foreach (KeyValuePair<FieldInfo, object> field in state.ConfigValues)
                    {
                        field.Key.SetValue(state.Config, field.Value);
                    }
                }
            }
        }

        private static void PrepareOriginalGroups(HeroController heroController)
        {
            RestoreOriginalGroups();
            if (originalGroupsHero == heroController)
            {
                return;
            }

            OriginalGroups.Clear();
            originalGroupsHero = heroController;
            sprintInAttack = null;
            nailArtsFsm = null;
            CachedDashObjects.Clear();
            CachedDashVariables.Clear();
            foreach (PlayMakerFSM fsm in heroController.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName == "Sprint")
                {
                    sprintInAttack = fsm.FsmVariables.FindFsmBool("In Attack");
                    foreach (HutongGames.PlayMaker.FsmState state in fsm.FsmStates)
                    {
                        foreach (HutongGames.PlayMaker.FsmStateAction action in state.Actions)
                        {
                            if (action is HutongGames.PlayMaker.Actions.GetHeroAttackObject attackObject)
                            {
                                CachedDashObjects.Add(attackObject);
                            }
                            else if (action is HutongGames.PlayMaker.Actions.GetHeroConfigVariable variable &&
                                (variable.VariableName.Value == nameof(HeroControllerConfig.DashStabSpeed) ||
                                 variable.VariableName.Value == nameof(HeroControllerConfig.DashStabTime) ||
                                 variable.VariableName.Value == nameof(HeroControllerConfig.DashStabSteps)))
                            {
                                CachedDashVariables.Add(variable);
                            }
                        }
                    }
                }
                else if (fsm.FsmName == "Nail Arts")
                {
                    nailArtsFsm = fsm;
                }
            }
            HeroController.ConfigGroup[] groups = ConfigsField?.GetValue(heroController) as HeroController.ConfigGroup[];
            if (groups == null)
            {
                return;
            }

            foreach (HeroController.ConfigGroup group in groups)
            {
                if (group == null || group.Config == null)
                {
                    continue;
                }

                OriginalGroupState state = new OriginalGroupState
                {
                    Group = group, Config = group.Config,
                    DashStab = group.DashStab, DashStabAlt = group.DashStabAlt,
                    ChargeSlash = group.ChargeSlash,
                };
                foreach (string name in OverriddenConfigFields)
                {
                    FieldInfo field = AccessTools.Field(typeof(HeroControllerConfig), name);
                    if (field != null)
                    {
                        state.ConfigValues[field] = field.GetValue(group.Config);
                    }
                }
                OriginalGroups.Add(state);
            }
        }

        private sealed class OriginalGroupState
        {
            internal HeroController.ConfigGroup Group;
            internal HeroControllerConfig Config;
            internal GameObject DashStab, DashStabAlt, ChargeSlash;
            internal readonly Dictionary<FieldInfo, object> ConfigValues = new Dictionary<FieldInfo, object>();
        }

        private static readonly HashSet<GameObject> ActivatedSourceRoots = new HashSet<GameObject>();
        private static readonly Stack<AttackContext> AttackContexts = new Stack<AttackContext>();
        private static AttackContext activeSpoofContext;

        private static HeroController downConfigHero;
        private static HeroControllerConfig downConfig;
        private static int downConfigDepth;
        private static HeroController animationConfigHero;
        private static HeroControllerConfig animationConfig;

        internal static void Apply(HeroController heroController)
        {
            if (Plugin.Instance == null || heroController == null)
            {
                return;
            }

            if (!Plugin.IsSupportedCrestId(Plugin.GetEquippedCrestId()))
            {
                Clear();
                return;
            }

            PrepareOriginalGroups(heroController);
            var usedRoots = new HashSet<GameObject>();
            ApplyNeutral(heroController, ResolveSourceGroup(heroController, Plugin.Instance.NeutralAttackSource), usedRoots);
            ApplyUp(heroController, ResolveSourceGroup(heroController, Plugin.Instance.UpAttackSource), usedRoots);
            ApplyDash(heroController, ResolveSourceGroup(heroController, Plugin.Instance.DashAttackSource), usedRoots);
            ApplyNeedleStrike(heroController, ResolveSourceGroup(heroController, Plugin.Instance.NeedleStrikeSource), usedRoots);

            HeroController.ConfigGroup downSourceGroup = ResolveSourceGroup(heroController, Plugin.Instance.DownAttackSource);
            if (downSourceGroup != null && downSourceGroup.Config != null && downSourceGroup.Config.DownSlashType != HeroControllerConfig.DownSlashTypes.Custom)
            {
                ApplyDown(heroController, downSourceGroup, usedRoots);
            }

            SyncSourceRoots(heroController.CurrentConfigGroup, usedRoots);
            RefreshCachedDashObjects(heroController);
        }

        internal static void BeginAttackOverride(HeroController heroController, AttackDirection attackDirection)
        {
            ClearAnimationConfigIfReady(heroController);
            HeroController.ConfigGroup sourceGroup = ResolveSourceGroup(heroController, GetAttackSource(attackDirection));
            if (heroController == null || sourceGroup == null || sourceGroup.Config == null)
            {
                AttackContexts.Push(null);
                return;
            }

            animationConfigHero = heroController;
            animationConfig = sourceGroup.Config;
            ActivateSourceRoot(sourceGroup, heroController.CurrentConfigGroup);

            if (attackDirection != AttackDirection.downward)
            {
                AttackContexts.Push(null);
                return;
            }

            if (sourceGroup.Config.DownSlashType == HeroControllerConfig.DownSlashTypes.Custom)
            {
                BeginCustomDownSpoof(heroController, sourceGroup);
                return;
            }

            downConfigHero = heroController;
            downConfig = sourceGroup.Config;
            downConfigDepth++;
            ApplyDown(heroController, sourceGroup, null);
            AttackContexts.Push(new AttackContext
            {
                HeroController = heroController,
                SourceGroup = sourceGroup,
                Mode = AttackOverrideMode.SimpleDownConfig,
            });
        }

        internal static void EndAttackOverride()
        {
            if (AttackContexts.Count == 0)
            {
                return;
            }

            AttackContext context = AttackContexts.Pop();
            if (context == null)
            {
                return;
            }

            if (context.Mode == AttackOverrideMode.SimpleDownConfig)
            {
                if (downConfigDepth > 0)
                {
                    downConfigDepth--;
                }
                return;
            }

            if (context.Mode == AttackOverrideMode.CustomDownSpoof && context.HeroController != null)
            {
                activeSpoofContext = context;
                SoftRestoreAttackSpoof(context);
                ActivateSourceRoot(context.SourceGroup, context.HeroController.CurrentConfigGroup);
                RestoreAttackSpoofIfReady(context.HeroController);
            }
        }

        internal static void RestoreAttackSpoofIfReady(HeroController heroController)
        {
            if (activeSpoofContext == null ||
                heroController == null ||
                heroController != activeSpoofContext.HeroController ||
                Time.time < activeSpoofContext.RestoreTime ||
                IsAnyAttackState(heroController))
            {
                return;
            }

            SoftRestoreAttackSpoof(activeSpoofContext);
            activeSpoofContext = null;
            downConfigHero = null;
            downConfig = null;
            downConfigDepth = 0;
            ClearAnimationConfigIfReady(heroController);
            Apply(heroController);
        }

        internal static bool TryGetActiveConfig(HeroController heroController, out HeroControllerConfig config)
        {
            config = null;
            if (heroController == null || heroController != downConfigHero || downConfig == null)
            {
                return false;
            }

            // A sprint attack can cancel down-spike recovery. Its config must
            // come from the equipped group with the selected dash fields.
            if (sprintInAttack != null && sprintInAttack.Value)
            {
                return false;
            }

            if (downConfigDepth > 0 || IsDownAttackState(heroController))
            {
                config = downConfig;
                return true;
            }

            return false;
        }

        internal static bool TryGetActiveAnimationClip(HeroAnimationController animationController, string clipName, out tk2dSpriteAnimationClip clip)
        {
            clip = null;
            if (animationController == null || !IsAttackAnimationClip(clipName))
            {
                return false;
            }

            HeroController heroController = AnimationHeroControllerField == null
                ? HeroController.UnsafeInstance
                : AnimationHeroControllerField.GetValue(animationController) as HeroController;
            if (heroController == null)
            {
                return false;
            }

            HeroControllerConfig config = null;
            if (heroController == originalGroupsHero && Plugin.Instance != null)
            {
                if (TryGetActiveConfig(heroController, out HeroControllerConfig downAttackConfig))
                {
                    config = downAttackConfig;
                }
                else if ((sprintInAttack != null && sprintInAttack.Value) ||
                    clipName == "Dash Upper Recovery" || clipName == "Dash Attack Recover")
                {
                    config = ResolveSourceGroup(heroController, Plugin.Instance.DashAttackSource)?.Config;
                }
                else if (nailArtsFsm != null && IsNeedleStrikeActive(nailArtsFsm.ActiveStateName))
                {
                    config = ResolveSourceGroup(heroController, Plugin.Instance.NeedleStrikeSource)?.Config;
                }
            }

            if (config == null && heroController == animationConfigHero && IsAnyAttackState(heroController))
            {
                config = animationConfig;
            }

            // Missing per-crest overrides use the base animation library, as they do
            // when that source crest is equipped. The equipped crest may override
            // the same name, so its already-resolved result is not a safe fallback.
            clip = config == null ? null : config.GetAnimationClip(clipName) ??
                animationController.animator.GetClipByName(clipName);
            return clip != null;
        }

        private static bool IsNeedleStrikeActive(string state)
        {
            switch (state)
            {
                case "Inactive":
                case "Idle":
                case "Init":
                case "FSM CANCEL":
                case "Cancel All":
                case "Can Nail Art?":
                    return false;
                default:
                    return !string.IsNullOrEmpty(state);
            }
        }

        internal static void ClearAnimationConfigIfReady(HeroController heroController)
        {
            if (heroController != null &&
                heroController == animationConfigHero &&
                !IsAnyAttackState(heroController))
            {
                animationConfigHero = null;
                animationConfig = null;
            }
        }

        private static void BeginCustomDownSpoof(HeroController heroController, HeroController.ConfigGroup sourceGroup)
        {
            var context = new AttackContext
            {
                HeroController = heroController,
                OriginalConfig = activeSpoofContext == null
                    ? CrestConfigField == null ? null : CrestConfigField.GetValue(heroController) as HeroControllerConfig
                    : activeSpoofContext.OriginalConfig,
                OriginalGroup = activeSpoofContext == null ? heroController.CurrentConfigGroup : activeSpoofContext.OriginalGroup,
                SourceGroup = sourceGroup,
                RestoreTime = Time.time + GetSpoofHoldTime(sourceGroup.Config),
                Mode = AttackOverrideMode.CustomDownSpoof,
            };

            downConfigHero = heroController;
            downConfig = sourceGroup.Config;
            downConfigDepth++;
            AttackContexts.Push(context);
            ActivateSourceRoot(sourceGroup, heroController.CurrentConfigGroup);
            CrestConfigField?.SetValue(heroController, sourceGroup.Config);
        }

        private static void SoftRestoreAttackSpoof(AttackContext context)
        {
            if (context == null || context.HeroController == null)
            {
                return;
            }

            CrestConfigField?.SetValue(context.HeroController, context.OriginalConfig);
            CurrentConfigGroupField?.SetValue(context.HeroController, context.OriginalGroup);
            context.HeroController.GetComponent<HeroAnimationController>()?.SetHeroControllerConfig(context.OriginalGroup?.Config);
            if (context.OriginalGroup != null && context.OriginalGroup.ActiveRoot != null)
            {
                context.OriginalGroup.ActiveRoot.SetActive(true);
            }
        }

        private static void RefreshCachedDashObjects(HeroController heroController)
        {
            HeroController.ConfigGroup group = heroController.CurrentConfigGroup;
            if (group == null)
            {
                return;
            }

            // Sprint caches these values when the equipped crest changes. Attack
            // source changes do not emit that event, and the vanilla event runs
            // before our SetConfigGroup postfix, so refresh the cached variables.
            foreach (HutongGames.PlayMaker.Actions.GetHeroAttackObject action in CachedDashObjects)
            {
                if (action.Attack == null || action.StoreAttack == null)
                {
                    continue;
                }

                switch ((HutongGames.PlayMaker.Actions.GetHeroAttackObject.AttackObjects)(object)action.Attack.Value)
                {
                    case HutongGames.PlayMaker.Actions.GetHeroAttackObject.AttackObjects.DashStab:
                        action.StoreAttack.Value = group.DashStab;
                        break;
                    case HutongGames.PlayMaker.Actions.GetHeroAttackObject.AttackObjects.DashStabAlt:
                        action.StoreAttack.Value = group.DashStabAlt;
                        break;
                }
            }

            foreach (HutongGames.PlayMaker.Actions.GetHeroConfigVariable variable in CachedDashVariables)
            {
                if (variable.StoreValue.IsNone)
                {
                    continue;
                }

                switch (variable.VariableName.Value)
                {
                    case nameof(HeroControllerConfig.DashStabSpeed):
                        variable.StoreValue.SetValue(group.Config.DashStabSpeed);
                        break;
                    case nameof(HeroControllerConfig.DashStabTime):
                        variable.StoreValue.SetValue(group.Config.DashStabTime);
                        break;
                    case nameof(HeroControllerConfig.DashStabSteps):
                        variable.StoreValue.SetValue(group.Config.DashStabSteps);
                        break;
                }
            }
        }

        private static void ApplyNeutral(HeroController heroController, HeroController.ConfigGroup sourceGroup, HashSet<GameObject> usedRoots)
        {
            if (sourceGroup == null)
            {
                return;
            }

            AddSourceRoot(sourceGroup, usedRoots);
            NormalSlashField?.SetValue(heroController, sourceGroup.NormalSlash);
            NormalSlashDamagerField?.SetValue(heroController, sourceGroup.NormalSlashDamager);
            AlternateSlashField?.SetValue(heroController, sourceGroup.AlternateSlash);
            AlternateSlashDamagerField?.SetValue(heroController, sourceGroup.AlternateSlashDamager);
        }

        private static void ApplyUp(HeroController heroController, HeroController.ConfigGroup sourceGroup, HashSet<GameObject> usedRoots)
        {
            if (sourceGroup == null)
            {
                return;
            }

            AddSourceRoot(sourceGroup, usedRoots);
            UpSlashField?.SetValue(heroController, sourceGroup.UpSlash);
            UpSlashDamagerField?.SetValue(heroController, sourceGroup.UpSlashDamager);
            AltUpSlashField?.SetValue(heroController, sourceGroup.AltUpSlash);
            AltUpSlashDamagerField?.SetValue(heroController, sourceGroup.AltUpSlashDamager);
        }

        private static void ApplyDown(HeroController heroController, HeroController.ConfigGroup sourceGroup, HashSet<GameObject> usedRoots)
        {
            if (sourceGroup == null)
            {
                return;
            }

            AddSourceRoot(sourceGroup, usedRoots);
            DownSpikeField?.SetValue(heroController, sourceGroup.Downspike);
            DownSlashField?.SetValue(heroController, sourceGroup.DownSlash);
            DownSlashDamagerField?.SetValue(heroController, sourceGroup.DownSlashDamager);
            AltDownSpikeField?.SetValue(heroController, sourceGroup.AltDownspike);
            AltDownSlashField?.SetValue(heroController, sourceGroup.AltDownSlash);
            AltDownSlashDamagerField?.SetValue(heroController, sourceGroup.AltDownSlashDamager);
        }

        private static void ApplyDash(HeroController heroController, HeroController.ConfigGroup sourceGroup, HashSet<GameObject> usedRoots)
        {
            if (sourceGroup == null || heroController == null || heroController.CurrentConfigGroup == null)
            {
                return;
            }

            AddSourceRoot(sourceGroup, usedRoots);
            DashStabField?.SetValue(heroController.CurrentConfigGroup, DashStabField.GetValue(sourceGroup));
            DashStabAltField?.SetValue(heroController.CurrentConfigGroup, DashStabAltField.GetValue(sourceGroup));
            CopyConfigFields(sourceGroup.Config, heroController.CurrentConfigGroup.Config, "dashStabSpeed", "dashStabTime", "forceShortDashStabBounce", "dashStabBounceJumpSpeed", "dashStabSteps", "canHarpoonDash");
        }

        private static void ApplyNeedleStrike(HeroController heroController, HeroController.ConfigGroup sourceGroup, HashSet<GameObject> usedRoots)
        {
            if (sourceGroup == null || heroController == null || heroController.CurrentConfigGroup == null)
            {
                return;
            }

            AddSourceRoot(sourceGroup, usedRoots);
            ChargeSlashField?.SetValue(heroController.CurrentConfigGroup, ChargeSlashField.GetValue(sourceGroup));
            CopyConfigFields(sourceGroup.Config, heroController.CurrentConfigGroup.Config, "chargeSlashRecoils", "chargeSlashLungeSpeed", "chargeSlashLungeDeceleration", "chargeSlashChain");
        }

        private static void CopyConfigFields(HeroControllerConfig source, HeroControllerConfig target, params string[] fieldNames)
        {
            if (source == null || target == null || fieldNames == null)
            {
                return;
            }

            for (int i = 0; i < fieldNames.Length; i++)
            {
                FieldInfo field = AccessTools.Field(typeof(HeroControllerConfig), fieldNames[i]);
                if (field != null)
                {
                    field.SetValue(target, field.GetValue(source));
                }
            }
        }

        private static AttackSource GetAttackSource(AttackDirection attackDirection)
        {
            if (Plugin.Instance == null)
            {
                return AttackSource.Hunter;
            }

            switch (attackDirection)
            {
                case AttackDirection.normal:
                    return Plugin.Instance.NeutralAttackSource;
                case AttackDirection.upward:
                    return Plugin.Instance.UpAttackSource;
                case AttackDirection.downward:
                    return Plugin.Instance.DownAttackSource;
                default:
                    return AttackSource.Hunter;
            }
        }

        private static HeroController.ConfigGroup ResolveSourceGroup(HeroController heroController, AttackSource attackSource)
        {
            if (heroController == null)
            {
                return null;
            }

            HeroController.ConfigGroup[] configs = ConfigsField == null
                ? null
                : ConfigsField.GetValue(heroController) as HeroController.ConfigGroup[];
            if (configs == null)
            {
                return null;
            }

            string[] crestNames = GetInternalCrestNames(attackSource);
            for (int i = 0; i < configs.Length; i++)
            {
                HeroController.ConfigGroup config = configs[i];
                if (config == null || config.Config == null)
                {
                    continue;
                }

                for (int j = 0; j < crestNames.Length; j++)
                {
                    if (config.Config.name == crestNames[j] ||
                        (config.ActiveRoot != null && config.ActiveRoot.name == crestNames[j]))
                    {
                        return config;
                    }
                }
            }

            return null;
        }

        private static bool IsDownAttackState(HeroController heroController)
        {
            return heroController != null &&
                (heroController.cState.downSpikeAntic ||
                heroController.cState.downSpiking ||
                heroController.cState.downSpikeRecovery ||
                heroController.cState.downSpikeBouncing ||
                heroController.cState.downAttacking);
        }

        private static bool IsAnyAttackState(HeroController heroController)
        {
            return heroController != null &&
                (heroController.cState.attacking ||
                heroController.cState.upAttacking ||
                IsDownAttackState(heroController));
        }

        private static bool IsAttackAnimationClip(string clipName)
        {
            switch (clipName)
            {
                case "Slash":
                case "SlashAlt":
                case "Slash Land":
                case "Slash Land Run":
                case "Slash Land Run Alt":
                case "Slash To Run":
                case "UpSlash":
                case "UpSlashAlt":
                case "DownSlash":
                case "DownSlashAlt":
                case "v3 Down Slash Antic":
                case "v3 Down Slash":
                case "DownSpike":
                case "DownSpike Antic":
                case "DownSpikeBounce 1":
                case "Downspike Recovery":
                case "Downspike Recovery Land":
                case "Charge":
                case "Charge Loop":
                case "Charge End":
                case "Charge Attack":
                case "ChargeSlash":
                case "ChargeSlashAlt":
                case "SpinBall Antic":
                case "SpinBall Launch":
                case "SpinBall":
                case "SpinBall End":
                case "Needle Strike":
                    return true;
                default:
                    return clipName != null &&
                        (clipName.IndexOf("Dash", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        clipName.IndexOf("Stab", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        clipName.IndexOf("Charge", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        clipName.IndexOf("Needle", System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        private static float GetSpoofHoldTime(HeroControllerConfig config)
        {
            if (config == null)
            {
                return 0.75f;
            }

            return Mathf.Max(0.75f, Mathf.Max(config.AttackDuration, config.AttackRecoveryTime) + 0.15f);
        }

        private static string[] GetInternalCrestNames(AttackSource attackSource)
        {
            switch (attackSource)
            {
                case AttackSource.Hunter:
                    return new[] { "Default", "Hunter", "Hunter_V1", "Hunter_V2", "Hunter_V3" };
                case AttackSource.Reaper:
                    return new[] { "Scythe", "Reaper" };
                case AttackSource.Wanderer:
                    return new[] { "Wanderer", "Dagger" };
                case AttackSource.Beast:
                    return new[] { "Warrior", "Beast" };
                case AttackSource.Witch:
                    return new[] { "Witch", "Whip" };
                case AttackSource.Architect:
                    return new[] { "Toolmaster", "Architect" };
                case AttackSource.Shaman:
                    return new[] { "Shaman", "Spell" };
                default:
                    return new[] { attackSource.ToString() };
            }
        }

        private static void AddSourceRoot(HeroController.ConfigGroup sourceGroup, HashSet<GameObject> usedRoots)
        {
            if (sourceGroup != null && sourceGroup.ActiveRoot != null && usedRoots != null)
            {
                usedRoots.Add(sourceGroup.ActiveRoot);
            }
        }

        private static void ActivateSourceRoot(HeroController.ConfigGroup sourceGroup, HeroController.ConfigGroup currentGroup)
        {
            if (sourceGroup == null || sourceGroup.ActiveRoot == null)
            {
                return;
            }

            GameObject currentRoot = currentGroup == null ? null : currentGroup.ActiveRoot;
            if (sourceGroup.ActiveRoot != currentRoot)
            {
                sourceGroup.ActiveRoot.SetActive(true);
                ActivatedSourceRoots.Add(sourceGroup.ActiveRoot);
            }
        }

        private static void SyncSourceRoots(HeroController.ConfigGroup currentGroup, HashSet<GameObject> usedRoots)
        {
            GameObject currentRoot = currentGroup == null ? null : currentGroup.ActiveRoot;
            foreach (GameObject root in ActivatedSourceRoots)
            {
                if (root != null && root != currentRoot && !usedRoots.Contains(root))
                {
                    root.SetActive(false);
                }
            }

            ActivatedSourceRoots.Clear();
            foreach (GameObject root in usedRoots)
            {
                if (root != null && root != currentRoot)
                {
                    root.SetActive(true);
                    ActivatedSourceRoots.Add(root);
                }
            }
        }

        internal static void Clear()
        {
            RestoreOriginalGroups();
            OriginalGroups.Clear();
            CachedDashObjects.Clear();
            CachedDashVariables.Clear();
            originalGroupsHero = null;
            sprintInAttack = null;
            nailArtsFsm = null;
            foreach (GameObject root in ActivatedSourceRoots)
            {
                if (root != null)
                {
                    root.SetActive(false);
                }
            }

            ActivatedSourceRoots.Clear();
            AttackContexts.Clear();
            activeSpoofContext = null;
            downConfigHero = null;
            downConfig = null;
            downConfigDepth = 0;
            animationConfigHero = null;
            animationConfig = null;
        }

        private enum AttackOverrideMode
        {
            SimpleDownConfig,
            CustomDownSpoof,
        }

        private sealed class AttackContext
        {
            internal HeroController HeroController;
            internal HeroControllerConfig OriginalConfig;
            internal HeroController.ConfigGroup OriginalGroup;
            internal HeroController.ConfigGroup SourceGroup;
            internal float RestoreTime;
            internal AttackOverrideMode Mode;
        }
    }
}
