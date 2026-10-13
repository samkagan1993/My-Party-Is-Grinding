using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using GameStruct;
using IdleMaster;
using UnityEngine;
//dotnet build -c Release IdleMasterMod.csproj
namespace IdleMasterMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.idlemastermod";
        public const string PluginName = "IdleMasterMod";
        public const string PluginVersion = "1.0.75";

        internal static BepInEx.Logging.ManualLogSource Log;

        // Separate plain-text log file just for AutoCombine / AutoCombineItems / AutoEquipBestGear
        // results, so they're easy to watch without the rest of BepInEx's LogOutput.log noise.
        private static readonly object _autoLogLock = new object();
        private static string _autoLogPath;

        private static void InitAutoLog()
        {
            try
            {
                _autoLogPath = Path.Combine(Paths.BepInExRootPath, "IdleMasterMod-AutoLog.log");
            }
            catch
            {
                _autoLogPath = "IdleMasterMod-AutoLog.log";
            }
        }

        private static bool _autoLogErrorReported;

        internal static void AutoLog(string line)
        {
            if (string.IsNullOrEmpty(_autoLogPath)) return;
            try
            {
                lock (_autoLogLock)
                {
                    File.AppendAllText(_autoLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                // Only report once so a persistently-failing write doesn't spam LogOutput.log.
                if (!_autoLogErrorReported)
                {
                    _autoLogErrorReported = true;
                    Log?.LogWarning($"AutoLog: couldn't write to '{_autoLogPath}'. {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        internal static ConfigEntry<bool> GodMode;
        internal static ConfigEntry<bool> ReduceIncomingDamage;
        internal static ConfigEntry<float> IncomingDamageMultiplier;
        internal static ConfigEntry<bool> OneHitKill;
        internal static ConfigEntry<bool> OutgoingDamageBoost;
        internal static ConfigEntry<float> OutgoingDamageMultiplier;
        internal static ConfigEntry<bool> NoSkillCooldown;
        internal static ConfigEntry<bool> ReduceSkillCooldown;
        internal static ConfigEntry<float> ReduceSkillCooldownBonus;
        internal static ConfigEntry<bool> AutoOpenChestAlwaysUnlocked;
        internal static ConfigEntry<bool> DisableSteamAchievements;
        internal static ConfigEntry<bool> FreezeEnemies;
        internal static ConfigEntry<bool> SlowEnemies;
        internal static ConfigEntry<float> SlowEnemiesMultiplier;
        internal static ConfigEntry<bool> PerfectDodgeBlock;
        internal static ConfigEntry<float> DodgeBlockChance;
        internal static ConfigEntry<bool> GeneralResistBoost;
        internal static ConfigEntry<float> GeneralResistBonus;
        internal static ConfigEntry<bool> FireResistBoost;
        internal static ConfigEntry<float> FireResistBonus;
        internal static ConfigEntry<bool> ColdResistBoost;
        internal static ConfigEntry<float> ColdResistBonus;
        internal static ConfigEntry<bool> LightningResistBoost;
        internal static ConfigEntry<float> LightningResistBonus;
        internal static ConfigEntry<bool> ChaosResistBoost;
        internal static ConfigEntry<float> ChaosResistBonus;
        internal static ConfigEntry<bool> LifestealBoost;
        internal static ConfigEntry<float> LifestealAmount;
        internal static ConfigEntry<bool> AddedDamageBoost;
        internal static ConfigEntry<float> AddedDamageAmount;
        internal static ConfigEntry<bool> PenetrationBoost;
        internal static ConfigEntry<float> PenetrationAmount;
        internal static ConfigEntry<bool> DefenseBoost;
        internal static ConfigEntry<float> DefenseAmount;
        internal static ConfigEntry<bool> AttackBoost;
        internal static ConfigEntry<float> AttackAmount;
        internal static ConfigEntry<bool> AutoEquipBestGear;
        internal static ConfigEntry<bool> AutoCombine;
        internal static ConfigEntry<bool> AutoCombineItems;
        internal static ConfigEntry<bool> AutoBuyOrbs;
        internal static ConfigEntry<bool> ClassUnlockFocus;
        internal static ConfigEntry<bool> AutoEngrave;
        internal static ConfigEntry<bool> AutoEnchant;
        internal static ConfigEntry<bool> AutoGem;
        internal static ConfigEntry<int> RequiredCombineMaterialCount;
        internal static ConfigEntry<float> CombineStuckCooldown;
        internal static ConfigEntry<int> CombineBandIndex;
        internal static ConfigEntry<float> ConfigManagerMaxHeight;

        private Harmony _harmony;
        private float _autoEquipBestGearTimer;
        private float _autoCombineTimer;
        private float _combineStuckCooldownTimer;
        private float _autoCombineItemsTimer;
        private int _autoCombineItemsRuns;
        private float _autoOrbBuyTimer;
        private int _autoOrbBuyCount;
        private float _autoSingleActionTimer;
        private int _autoEngraveRuns;
        private int _autoEnchantRuns;
        private int _autoGemRuns;

        private static readonly FieldInfo _cubeModeField =
            typeof(CubePopup).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeAutoFillMethod =
            typeof(CubePopup).GetMethod("AutoFill", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeDoSynthesizeMethod =
            typeof(CubePopup).GetMethod("DoSynthesize", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeGridFullMethod =
            typeof(CubePopup).GetMethod("GridFull", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeClearGridMethod =
            typeof(CubePopup).GetMethod("ClearGrid", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeGridCountMethod =
            typeof(CubePopup).GetMethod("GridCount", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeToggleMaterialMethod =
            typeof(CubePopup).GetMethod("ToggleMaterialInGrid", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeGridHasGearMethod =
            typeof(CubePopup).GetMethod("GridHasGear", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeGridHasMaterialMethod =
            typeof(CubePopup).GetMethod("GridHasMaterial", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo _cubeAllowedMaterialCountOfMethod =
            typeof(CubePopup).GetMethod("AllowedMaterialCountOf", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeGridMaterialUnitsMethod =
            typeof(CubePopup).GetMethod("GridMaterialUnits", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _cubeIncludeWarehouseField =
            typeof(CubePopup).GetField("_includeWarehouse", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo _cubeAutoFillSingleMethod =
            typeof(CubePopup).GetMethod("AutoFillSingle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeDoExecuteSingleMethod =
            typeof(CubePopup).GetMethod("DoExecuteSingle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _cubeTargetGearField =
            typeof(CubePopup).GetField("_targetGear", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo _cubeInstanceField =
            typeof(CubePopup).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo _cubeRangeIdxField =
            typeof(CubePopup).GetField("_rangeIdx", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _cubeClampRangeMethod =
            typeof(CubePopup).GetMethod("ClampRange", BindingFlags.NonPublic | BindingFlags.Instance);

        // CubePopup is never destroyed when closed, only SetActive(false) — so the cached
        // static _instance stays valid (and its private logic still callable) even while
        // the panel is hidden. Requires the player to have opened Blacksmith > Combine at
        // least once this session so _instance gets assigned in the first place.
        private static CubePopup GetCubePopupInstance()
        {
            return _cubeInstanceField?.GetValue(null) as CubePopup;
        }

        // --- Log name/grade resolution helpers ---

        internal static CommonData GetCommonData()
        {
            var player = SingletonObject<Player>.Instance;
            if (player == null) return null;
            return _playerCommonDataField?.GetValue(player) as CommonData;
        }

        private static string ResolveAliasText(string alias)
        {
            if (string.IsNullOrEmpty(alias)) return alias;
            try
            {
                var dataText = SingletonObject<DataText>.Instance;
                string text = dataText?.GetText(alias);
                if (!string.IsNullOrEmpty(text) && text != alias) return text;
            }
            catch { }
            return alias;
        }

        private static string ToTitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.ToLowerInvariant();
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        internal static string GetGearName(int gearId)
        {
            try
            {
                var commonData = GetCommonData();
                if (commonData?.gearData != null && commonData.gearData.TryGetValue(gearId, out var data) && data != null)
                {
                    if (!string.IsNullOrEmpty(data.NameAlias)) return ResolveAliasText(data.NameAlias);
                }
            }
            catch { }
            return $"Gear #{gearId}";
        }

        internal static string GetGradeName(int gradeIndex)
        {
            try
            {
                var commonData = GetCommonData();
                string raw = commonData?.GradeIndexToString(gradeIndex);
                if (!string.IsNullOrEmpty(raw)) return ToTitleCase(raw);
            }
            catch { }
            return $"Grade {gradeIndex}";
        }

        internal static string GetMaterialName(ItemType itemType, int itemId)
        {
            try
            {
                var commonData = GetCommonData();
                string alias = null;
                switch (itemType)
                {
                    case ItemType.GROCERY:
                        alias = commonData?.GetGroceryData(itemId)?.NameAlias;
                        break;
                    case ItemType.GEM:
                        alias = commonData?.GetGemData(itemId)?.NameAlias;
                        break;
                    case ItemType.AWAKEN_MATERIAL:
                        alias = commonData?.GetAwakenMaterialData(itemId)?.NameAlias;
                        break;
                }
                if (!string.IsNullOrEmpty(alias)) return ResolveAliasText(alias);
            }
            catch { }
            return itemType == ItemType.SCROLL ? $"Scroll #{itemId}" : $"{itemType} #{itemId}";
        }

        private int _nextConfigOrder = 1000;

        private ConfigEntry<T> BindOrdered<T>(string section, string key, T defaultValue, string description)
        {
            var tag = new ConfigurationManagerAttributes { Order = _nextConfigOrder-- };
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, null, tag));
        }

        private void Awake()
        {
            Log = Logger;
            InitAutoLog();

            GodMode = BindOrdered("Cheats", "GodMode", false,
                "No damage taken.");
            ReduceIncomingDamage = BindOrdered("Cheats", "ReduceIncomingDamage", false,
                "Reduce incoming damage (ignored if GodMode is on).");
            IncomingDamageMultiplier = BindOrdered("Cheats", "IncomingDamageMultiplier", 0.1f,
                "Incoming damage multiplier. 1.0 = normal, 0.1 = 10% damage taken.");
            OneHitKill = BindOrdered("Cheats", "OneHitKill", false,
                "Kill enemies in one hit.");
            OutgoingDamageBoost = BindOrdered("Cheats", "OutgoingDamageBoost", false,
                "Boost outgoing damage (ignored if OneHitKill is on).");
            OutgoingDamageMultiplier = BindOrdered("Cheats", "OutgoingDamageMultiplier", 2.0f,
                "Outgoing damage multiplier. 1.0 = normal, 2.0 = double.");
            NoSkillCooldown = BindOrdered("Cheats", "NoSkillCooldown", false,
                "Skills always ready, no cooldown.");
            ReduceSkillCooldown = BindOrdered("Cheats", "ReduceSkillCooldown", false,
                "Speed up skill cooldowns (ignored if NoSkillCooldown is on).");
            ReduceSkillCooldownBonus = BindOrdered("Cheats", "ReduceSkillCooldownBonus", 0.5f,
                "Extra cooldown speed. 0.5 = ~50% faster, 1.0 = ~2x faster.");
            AutoOpenChestAlwaysUnlocked = BindOrdered("Cheats", "AutoOpenChestAlwaysUnlocked", false,
                "Auto-open every chest type without needing the rune unlock, faster interval.");
            DisableSteamAchievements = BindOrdered("Cheats", "DisableSteamAchievements", false,
                "Block Steam achievements.");
            FreezeEnemies = BindOrdered("Cheats", "FreezeEnemies", false,
                "Enemies can't move or attack.");
            SlowEnemies = BindOrdered("Cheats", "SlowEnemies", false,
                "Slow enemy move/attack speed.");
            SlowEnemiesMultiplier = BindOrdered("Cheats", "SlowEnemiesMultiplier", 0.1f,
                "Enemy speed multiplier. 1.0 = normal, 0.1 = 10x slower.");
            PerfectDodgeBlock = BindOrdered("Cheats", "PerfectDodgeBlock", false,
                "Chance to force enemy miss and force your own dodge/block.");
            DodgeBlockChance = BindOrdered("Cheats", "DodgeBlockChance", 0.75f,
                "Chance (0.0-1.0) for PerfectDodgeBlock to trigger.");
            GeneralResistBoost = BindOrdered("Cheats", "GeneralResistBoost", false,
                "Fixed resist for elements without their own toggle below.");
            GeneralResistBonus = BindOrdered("Cheats", "GeneralResistBonus", 30f,
                "General resist value. 30 = 30%.");
            FireResistBoost = BindOrdered("Cheats", "FireResistBoost", false,
                "Fixed Fire resist (overrides General for Fire).");
            FireResistBonus = BindOrdered("Cheats", "FireResistBonus", 30f,
                "Fire resist value. 30 = 30%.");
            ColdResistBoost = BindOrdered("Cheats", "ColdResistBoost", false,
                "Fixed Cold resist (overrides General for Cold).");
            ColdResistBonus = BindOrdered("Cheats", "ColdResistBonus", 30f,
                "Cold resist value. 30 = 30%.");
            LightningResistBoost = BindOrdered("Cheats", "LightningResistBoost", false,
                "Fixed Lightning resist (overrides General for Lightning).");
            LightningResistBonus = BindOrdered("Cheats", "LightningResistBonus", 30f,
                "Lightning resist value. 30 = 30%.");
            ChaosResistBoost = BindOrdered("Cheats", "ChaosResistBoost", false,
                "Fixed Chaos resist (overrides General for Chaos).");
            ChaosResistBonus = BindOrdered("Cheats", "ChaosResistBonus", 30f,
                "Chaos resist value. 30 = 30%.");
            LifestealBoost = BindOrdered("Cheats", "LifestealBoost", false,
                "Fixed Lifesteal value.");
            LifestealAmount = BindOrdered("Cheats", "LifestealAmount", 30f,
                "Lifesteal value. 30 = 30% healed back.");
            AddedDamageBoost = BindOrdered("Cheats", "AddedDamageBoost", false,
                "Fixed AdditionalDamage percentage.");
            AddedDamageAmount = BindOrdered("Cheats", "AddedDamageAmount", 500f,
                "AdditionalDamage percentage. 500 = +500%.");
            PenetrationBoost = BindOrdered("Cheats", "PenetrationBoost", false,
                "Fixed Penetration value.");
            PenetrationAmount = BindOrdered("Cheats", "PenetrationAmount", 100f,
                "Penetration value.");
            DefenseBoost = BindOrdered("Cheats", "DefenseBoost", false,
                "Multiply your Defense.");
            DefenseAmount = BindOrdered("Cheats", "DefenseAmount", 2f,
                "Defense multiplier. 2.0 = double.");
            AttackBoost = BindOrdered("Cheats", "AttackBoost", false,
                "Multiply your base Attack.");
            AttackAmount = BindOrdered("Cheats", "AttackAmount", 2f,
                "Attack multiplier. 2.0 = double.");
            AutoEquipBestGear = BindOrdered("Cheats", "AutoEquipBestGear", false,
                "Auto-equip the best owned gear on every hero.");
            AutoCombine = BindOrdered("Cheats", "AutoCombine", false,
                "Auto-combine gear in the Blacksmith Cube. Open Blacksmith > Combine once (pick the band) to initialize it, then it keeps running even with the menu closed.");
            AutoCombineItems = BindOrdered("Cheats", "AutoCombineItems", false,
                "Auto-combine materials (Grocery/Gem/Awaken/Scroll) — works even with the Cube closed.");
            AutoBuyOrbs = BindOrdered("Cheats", "AutoBuyOrbs", false,
                "Auto-buy every affordable Rune/Orb node.");
            ClassUnlockFocus = BindOrdered("Cheats", "ClassUnlockFocus", true,
                "While AutoBuyOrbs is on, prioritize the 4 class-unlock nodes first.");
            AutoEngrave = BindOrdered("Cheats", "AutoEngrave", false,
                "Auto-engrave in the Blacksmith Cube (popup must be open, Engrave tab, target selected).");
            AutoEnchant = BindOrdered("Cheats", "AutoEnchant", false,
                "Auto-enchant in the Blacksmith Cube (same as AutoEngrave, Enchant tab).");
            AutoGem = BindOrdered("Cheats", "AutoGem", false,
                "Auto-socket gems in the Blacksmith Cube (same as AutoEngrave, Gem tab).");
            RequiredCombineMaterialCount = BindOrdered("Cheats", "RequiredCombineMaterialCount", 9,
                "How many matching gear/material pieces needed to combine (9 by default).");
            CombineStuckCooldown = BindOrdered("Cheats", "CombineStuckCooldown", 10f,
                "Seconds to wait before retrying after a failed combine attempt.");
            CombineBandIndex = BindOrdered("Cheats", "CombineBandIndex", -1,
                "Which Blacksmith Combine band (tier) AutoCombine should use, 0-based. -1 = don't override, use whatever's currently/last selected in the Combine screen. Auto-falls back to band 0 if the chosen band isn't unlocked yet.");
            ConfigManagerMaxHeight = BindOrdered("Cheats", "ConfigManagerMaxHeight", 650f,
                "Max height (px) of the ConfigurationManager 'Plugin / mod settings' window, if installed.");

            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
                Log?.LogInfo("[DIAG] Harmony.PatchAll completed without throwing.");
            }
            catch (Exception ex)
            {
                Log?.LogError($"[DIAG] Harmony.PatchAll THREW: {ex}");
            }

            bool synthPatched = false;
            try
            {
                var original = AccessTools.Method(typeof(Player), "SynthesizeGears");
                synthPatched = original != null && Harmony.GetPatchInfo(original)?.Postfixes.Count > 0;
            }
            catch { }
            Log?.LogInfo($"[DIAG] Player.SynthesizeGears patched = {synthPatched}");

            TryPatchConfigManagerWindowSize();
        }

        private void TryPatchConfigManagerWindowSize()
        {
            try
            {
                Type cmType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    cmType = asm.GetType("ConfigurationManager.ConfigurationManager");
                    if (cmType != null) break;
                }
                if (cmType == null) return;

                var calcMethod = cmType.GetMethod("CalculateWindowRect", BindingFlags.NonPublic | BindingFlags.Instance);
                if (calcMethod == null) return;

                var postfix = typeof(Plugin).GetMethod(nameof(ConfigManagerWindowRectPostfix), BindingFlags.NonPublic | BindingFlags.Static);
                _harmony.Patch(calcMethod, postfix: new HarmonyMethod(postfix));
            }
            catch
            {
            }
        }

        private static void ConfigManagerWindowRectPostfix(object __instance)
        {
            try
            {
                var rectProp = __instance.GetType().GetProperty("SettingWindowRect",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (rectProp == null) return;

                var rect = (Rect)rectProp.GetValue(__instance);
                float maxHeight = ConfigManagerMaxHeight.Value;
                if (maxHeight > 0f && rect.height > maxHeight)
                {
                    float newY = Mathf.RoundToInt((Screen.height - maxHeight) / 2f);
                    rectProp.SetValue(__instance, new Rect(rect.x, newY, rect.width, maxHeight));
                }
            }
            catch
            {
            }
        }

        private void Update()
        {
            if (ReduceSkillCooldown.Value && !NoSkillCooldown.Value)
            {
                float targets = ReduceSkillCooldownBonus.Value * Time.deltaTime;
                if (targets > 0f)
                {
                    foreach (var soldier in FindObjectsByType<Soldier>())
                    {
                        soldier.changeSkillCooltime(targets);
                    }
                }
            }

            if (AutoEquipBestGear.Value)
            {
                _autoEquipBestGearTimer -= Time.deltaTime;
                if (_autoEquipBestGearTimer <= 0f)
                {
                    _autoEquipBestGearTimer = 2.0f;
                    RunAutoEquipBestGear();
                }
            }

            if (AutoCombine.Value)
            {
                _autoCombineTimer -= Time.deltaTime;
                if (_autoCombineTimer <= 0f)
                {
                    _autoCombineTimer = 1.0f;
                    RunAutoCombineTick();
                }
            }

            if (AutoCombineItems.Value)
            {
                _autoCombineItemsTimer -= Time.deltaTime;
                if (_autoCombineItemsTimer <= 0f)
                {
                    _autoCombineItemsTimer = 1.0f;
                    RunAutoCombineItemsTick();
                }
            }

            if (AutoBuyOrbs.Value)
            {
                _autoOrbBuyTimer -= Time.deltaTime;
                if (_autoOrbBuyTimer <= 0f)
                {
                    _autoOrbBuyTimer = 2.0f;
                    RunAutoBuyOrbsTick();
                }
            }

            if ((AutoEngrave.Value || AutoEnchant.Value || AutoGem.Value) && CubePopup.IsOpen)
            {
                _autoSingleActionTimer -= Time.deltaTime;
                if (_autoSingleActionTimer <= 0f)
                {
                    _autoSingleActionTimer = 1.0f;
                    RunAutoSingleActionTick();
                }
            }
        }

        private void RunAutoCombineTick()
        {
            if (!AutoCombine.Value) return;
            if (_cubeModeField == null || _cubeAutoFillMethod == null ||
                _cubeDoSynthesizeMethod == null || _cubeGridFullMethod == null)
            {
                return;
            }

            if (_combineStuckCooldownTimer > 0f)
            {
                _combineStuckCooldownTimer -= 1.0f;
                return;
            }

            CubePopup cube = GetCubePopupInstance();
            if (cube == null) return;

            object modeValue = _cubeModeField.GetValue(cube);
            if (modeValue == null || modeValue.ToString() != "SYNTHESIS") return;

            if (AutoEquipBestGear.Value) RunAutoEquipBestGear();

            int wantedBand = CombineBandIndex.Value;
            if (wantedBand >= 0 && _cubeRangeIdxField != null)
            {
                int band = Math.Min(wantedBand, CubeBandRules.BAND_COUNT - 1);
                _cubeRangeIdxField.SetValue(cube, band);
                _cubeClampRangeMethod?.Invoke(cube, null);
            }

            _cubeClearGridMethod?.Invoke(cube, new object[] { true });

            _cubeAutoFillMethod.Invoke(cube, null);
            bool full = (bool)_cubeGridFullMethod.Invoke(cube, null);

            if (!full)
            {
                _cubeClearGridMethod?.Invoke(cube, new object[] { true });
                _combineStuckCooldownTimer = CombineStuckCooldown.Value;
                return;
            }

            try
            {
                _cubeDoSynthesizeMethod.Invoke(cube, null);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"AutoCombine: DoSynthesize threw, forcing grid clear. {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _cubeClearGridMethod?.Invoke(cube, new object[] { true });
            }
        }

        private static readonly ItemType[] _materialItemTypes =
            { ItemType.GROCERY, ItemType.GEM, ItemType.AWAKEN_MATERIAL, ItemType.SCROLL };

        private void RunAutoCombineItemsTick()
        {
            if (!AutoCombineItems.Value) return;

            var player = SingletonObject<Player>.Instance;
            var storage = player?.Storage;
            var itemList = storage?.ItemList;
            if (itemList == null || player == null) return;

            var totals = new Dictionary<(ItemType type, int id), long>();
            foreach (var materialType in _materialItemTypes)
            {
                if (!itemList.TryGetValue(materialType, out var idMap) || idMap == null) continue;
                foreach (var idEntry in idMap)
                {
                    var prop = idEntry.Value;
                    if (prop == null) continue;
                    totals[(materialType, idEntry.Key)] = prop.Count;
                }
            }

            var candidates = new List<KeyValuePair<(ItemType type, int id), long>>(totals);
            candidates.Sort((a, b) => b.Value.CompareTo(a.Value));

            foreach (var kv in candidates)
            {
                if (kv.Value < RequiredCombineMaterialCount.Value) continue;
                ItemType itemType = kv.Key.type;
                int itemId = kv.Key.id;

                int gradeIdx = -1;
                try { gradeIdx = player.GetMaterialGradeIndex(itemType, itemId); } catch { }
                if (gradeIdx >= 7) continue;

                int nextResultCount = -1;
                try
                {
                    var resultList = player.GetMaterialSynthesisResultIds(itemType, itemId, 1);
                    nextResultCount = resultList?.Count ?? 0;
                }
                catch { }
                if (nextResultCount == 0) continue;

                int resultId = 0;
                try { resultId = player.SynthesizeMaterials(itemType, itemId, true); } catch { }
                if (resultId > 0)
                {
                    _autoCombineItemsRuns++;

                    string beforeName = GetMaterialName(itemType, itemId);
                    string beforeGrade = GetGradeName(gradeIdx);
                    string afterName = GetMaterialName(itemType, resultId);
                    int afterGradeIdx = gradeIdx;
                    try { afterGradeIdx = player.GetMaterialGradeIndex(itemType, resultId); } catch { }
                    string afterGrade = GetGradeName(afterGradeIdx);
                    AutoLog($"AutoCombineItems: success -> {beforeName} [{beforeGrade}] -> {afterName} [{afterGrade}]");

                    return;
                }
            }
        }

        private static readonly RuneEffectType[] ClassUnlockKeys =
        {
            RuneEffectType.CLASS_UNLOCK_WARRIOR, RuneEffectType.CLASS_UNLOCK_ASSASSIN,
            RuneEffectType.CLASS_UNLOCK_ARCHER, RuneEffectType.CLASS_UNLOCK_MAGE
        };

        private static readonly FieldInfo _playerCommonDataField =
            typeof(Player).GetField("commonData", BindingFlags.NonPublic | BindingFlags.Instance);

        private void RunAutoBuyOrbsTick()
        {
            var player = SingletonObject<Player>.Instance;
            if (player == null) return;

            var commonData = _playerCommonDataField?.GetValue(player) as CommonData;
            if (commonData == null) return;

            var runeNodes = commonData.runeData;
            if (runeNodes == null) return;

            bool classUnlockPending = false;
            if (ClassUnlockFocus.Value)
            {
                foreach (var kv in runeNodes)
                {
                    if (Array.IndexOf(ClassUnlockKeys, kv.Value.Effect) >= 0 && !player.IsRuneMaxed(kv.Key))
                    {
                        classUnlockPending = true;
                        break;
                    }
                }
            }

            var seedKeys = new List<int>();
            if (classUnlockPending)
            {
                foreach (var kv in runeNodes)
                {
                    if (Array.IndexOf(ClassUnlockKeys, kv.Value.Effect) >= 0) seedKeys.Add(kv.Key);
                }
            }
            else
            {
                seedKeys.AddRange(runeNodes.Keys);
            }

            var visited = new HashSet<int>();
            var queue = new Queue<int>(seedKeys);

            int bestKey = -1;
            double bestCost = double.MaxValue;

            while (queue.Count > 0)
            {
                int key = queue.Dequeue();
                if (!visited.Add(key)) continue;

                if (player.IsRuneMaxed(key)) continue;

                runeNodes.TryGetValue(key, out var node);
                if (node != null && node.Parent > 0 && !player.IsRuneMaxed(node.Parent))
                {
                    queue.Enqueue(node.Parent);
                    continue;
                }

                if (!player.CanBuyRune(key)) continue;

                double cost = player.GetRuneNextCost(key);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestKey = key;
                }
            }

            if (bestKey >= 0 && player.BuyRune(bestKey, bestCost))
            {
                _autoOrbBuyCount++;
            }
        }

        private void RunAutoSingleActionTick()
        {
            if (_cubeModeField == null || _cubeAutoFillSingleMethod == null ||
                _cubeDoExecuteSingleMethod == null || _cubeTargetGearField == null)
            {
                return;
            }

            CubePopup cube = null;
            foreach (var c in FindObjectsByType<CubePopup>())
            {
                cube = c;
                break;
            }
            if (cube == null) return;

            object modeValue = _cubeModeField.GetValue(cube);
            if (modeValue == null) return;
            var mode = (CubePopup.Mode)modeValue;

            bool enabled;
            switch (mode)
            {
                case CubePopup.Mode.ENGRAVE: enabled = AutoEngrave.Value; break;
                case CubePopup.Mode.ENCHANT: enabled = AutoEnchant.Value; break;
                case CubePopup.Mode.GEM: enabled = AutoGem.Value; break;
                default: return;
            }
            if (!enabled) return;

            if (mode == CubePopup.Mode.ENCHANT)
            {
                var dataGame = SingletonObject<DataGame>.Instance;
                var stored = dataGame != null ? dataGame.clientStoredData : null;
                if (stored != null && !stored.EnchantConfirmDisabled)
                {
                    stored.EnchantConfirmDisabled = true;
                }
            }

            object targetGear = _cubeTargetGearField.GetValue(cube);
            if (targetGear == null) return;

            _cubeAutoFillSingleMethod.Invoke(cube, null);
            _cubeDoExecuteSingleMethod.Invoke(cube, null);

            switch (mode)
            {
                case CubePopup.Mode.ENGRAVE: _autoEngraveRuns++; break;
                case CubePopup.Mode.ENCHANT: _autoEnchantRuns++; break;
                case CubePopup.Mode.GEM: _autoGemRuns++; break;
            }
        }

        private void RunAutoEquipBestGear()
        {
            var player = SingletonObject<Player>.Instance;
            if (player == null) return;
            var storage = player.Storage;
            if (storage == null) return;
            var allGear = storage.GetGearInventory();
            if (allGear == null) return;

            CreatureType[] heroes =
            {
                CreatureType.WARRIOR, CreatureType.ASSASSIN, CreatureType.KNIGHT,
                CreatureType.ARCHER, CreatureType.MAGE
            };

            foreach (var hero in heroes)
            {
                string heroName = hero.ToString();
                var bestPerSlot = new Dictionary<string, GearInstanceProperty>();

                foreach (var g in allGear)
                {
                    if (g == null || string.IsNullOrEmpty(g.SlotType)) continue;
                    if (!string.IsNullOrEmpty(g.CreatureType) && g.CreatureType != "COMMON" && g.CreatureType != heroName) continue;

                    if (g.EquippedCreatureType != CreatureType.NONE && g.EquippedCreatureType != hero) continue;

                    if (!bestPerSlot.TryGetValue(g.SlotType, out var cur) || IsBetterGear(g, cur))
                    {
                        bestPerSlot[g.SlotType] = g;
                    }
                }

                foreach (var kv in bestPerSlot)
                {
                    var best = kv.Value;
                    var equipped = storage.GetEquippedGearBySlot(hero, kv.Key);
                    if (equipped != null && equipped.InstanceId == best.InstanceId) continue;
                    if (equipped != null && !IsBetterGear(best, equipped)) continue;

                    bool ok = false;
                    try { ok = player.EquipGear(best.InstanceId, hero); } catch { }
                    if (!ok) continue;

                    string before = equipped != null
                        ? $"{GetGearName(equipped.GearId)} Lv.{equipped.Level} [{GetGradeName(equipped.GradeTier)}]"
                        : "(none)";
                    string after = $"{GetGearName(best.GearId)} Lv.{best.Level} [{GetGradeName(best.GradeTier)}]";
                    AutoLog($"AutoEquipBestGear: {heroName} {kv.Key} — before: {before} -> after: {after}");
                }
            }
        }

        private static bool IsBetterGear(GearInstanceProperty a, GearInstanceProperty b)
        {
            if (a.GradeTier != b.GradeTier) return a.GradeTier > b.GradeTier;
            if (a.Level != b.Level) return a.Level > b.Level;
            return a.EnhancementStep > b.EnhancementStep;
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
    }

    [HarmonyPatch(typeof(Player), "SynthesizeGears")]
    internal static class Patch_Player_SynthesizeGears_Log
    {
        static void Postfix(GearInstanceProperty __result)
        {
            // TEMP diagnostic: always print to the main log, unconditionally, so we can
            // confirm this patch actually fires at all (manual combine or AutoCombine).
            Plugin.Log?.LogInfo($"[DIAG] SynthesizeGears postfix fired. result={(object)__result ?? "null"} instanceId={__result?.InstanceId}");

            if (!Plugin.AutoCombine.Value) return;
            if (__result == null || __result.InstanceId == 0) return;

            string name = Plugin.GetGearName(__result.GearId);
            string grade = Plugin.GetGradeName(__result.GradeTier);
            Plugin.AutoLog($"AutoCombine: success -> {name} Lv.{__result.Level} [{grade}]");
        }
    }

    [HarmonyPatch(typeof(Character), "SetDamage")]
    internal static class Patch_Character_SetDamage
    {
        static bool Prefix(Character __instance, object[] __args)
        {
            if (Plugin.GodMode.Value) return false;
            if (Plugin.ReduceIncomingDamage.Value)
            {
                double damage = (double)__args[0];
                __args[0] = damage * Plugin.IncomingDamageMultiplier.Value;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Enemy), "SetDamage")]
    internal static class Patch_Enemy_SetDamage
    {
        static void Prefix(Enemy __instance, ref double __0)
        {
            if (Plugin.OneHitKill.Value)
            {
                __0 = 999999999.0;
                return;
            }
            if (Plugin.OutgoingDamageBoost.Value)
            {
                __0 *= Plugin.OutgoingDamageMultiplier.Value;
            }
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "checkSkillActivation")]
    internal static class Patch_BattleUnit_checkSkillActivation
    {
        static bool Prefix(BattleUnit __instance, ref bool __result)
        {
            if (!Plugin.NoSkillCooldown.Value) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "CheckSkillInvalidation")]
    internal static class Patch_BattleUnit_CheckSkillInvalidation
    {
        static bool Prefix(BattleUnit __instance, ref bool __result)
        {
            if (!Plugin.NoSkillCooldown.Value) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BattleRandomBoxHud), "isAutoOpenDisabled")]
    internal static class Patch_BattleRandomBoxHud_isAutoOpenDisabled
    {
        static bool Prefix(ref bool __result)
        {
            if (!Plugin.AutoOpenChestAlwaysUnlocked.Value) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "HasRuneEffect")]
    internal static class Patch_Player_HasRuneEffect
    {
        static readonly HashSet<int> AutoOpenRuneIds = new HashSet<int> { 23, 24, 25 };

        static bool Prefix(RuneEffectType effect, ref bool __result)
        {
            if (!Plugin.AutoOpenChestAlwaysUnlocked.Value) return true;
            if (!AutoOpenRuneIds.Contains((int)effect)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BattleRandomBoxHud), "autoOpenInterval")]
    internal static class Patch_BattleRandomBoxHud_autoOpenInterval
    {
        static void Postfix(ref float __result)
        {
            if (!Plugin.AutoOpenChestAlwaysUnlocked.Value) return;
            __result = 1.0f;
        }
    }

    [HarmonyPatch(typeof(SteamAchievements), "Grant")]
    internal static class Patch_SteamAchievements_Grant
    {
        static bool Prefix()
        {
            return !Plugin.DisableSteamAchievements.Value;
        }
    }

    [HarmonyPatch(typeof(Enemy), "move")]
    internal static class Patch_Enemy_Move_Freeze
    {
        static bool Prefix(Enemy __instance)
        {
            if (!Plugin.FreezeEnemies.Value) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "checkNormalAttack")]
    internal static class Patch_BattleUnit_CheckNormalAttack_Freeze
    {
        static bool Prefix(BattleUnit __instance)
        {
            if (!Plugin.FreezeEnemies.Value) return true;
            if (!(__instance is Enemy)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "GetChillSpeedMult")]
    internal static class Patch_BattleUnit_GetChillSpeedMult_Slow
    {
        static void Postfix(BattleUnit __instance, ref float __result)
        {
            if (!Plugin.SlowEnemies.Value) return;
            if (!(__instance is Enemy)) return;
            __result = Plugin.SlowEnemiesMultiplier.Value;
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "RollDodgeBlock")]
    internal static class Patch_BattleUnit_RollDodgeBlock
    {
        static void Postfix(BattleUnit __instance, ref bool __result, ref double blockMult)
        {
            if (!Plugin.PerfectDodgeBlock.Value) return;

            float chance = Plugin.DodgeBlockChance.Value;
            if (UnityEngine.Random.Range(0f, 1f) >= chance) return;

            if (__instance is Enemy)
            {
                __result = false;
                blockMult = 1;
            }
            else if (__instance is Character)
            {
                __result = true;
                blockMult = 0;
            }
        }
    }

    [HarmonyPatch(typeof(Skill), "applyDamage")]
    internal static class Patch_Skill_applyDamage_ContextFlag
    {
        internal static bool IsTargetEnemy;

        static void Prefix(BattleUnit target) => IsTargetEnemy = target is Enemy;
    }

    [HarmonyPatch(typeof(BattleUnit), "GetElementResistPct")]
    internal static class Patch_BattleUnit_GetElementResistPct
    {
        static void Postfix(ElementType e, ref double __result)
        {
            if (Patch_Skill_applyDamage_ContextFlag.IsTargetEnemy) return;

            switch (e)
            {
                case ElementType.FIRE:
                    if (Plugin.FireResistBoost.Value) __result = Plugin.FireResistBonus.Value;
                    else if (Plugin.GeneralResistBoost.Value) __result = Plugin.GeneralResistBonus.Value;
                    break;
                case ElementType.COLD:
                    if (Plugin.ColdResistBoost.Value) __result = Plugin.ColdResistBonus.Value;
                    else if (Plugin.GeneralResistBoost.Value) __result = Plugin.GeneralResistBonus.Value;
                    break;
                case ElementType.LIGHTNING:
                    if (Plugin.LightningResistBoost.Value) __result = Plugin.LightningResistBonus.Value;
                    else if (Plugin.GeneralResistBoost.Value) __result = Plugin.GeneralResistBonus.Value;
                    break;
                case ElementType.CHAOS:
                    if (Plugin.ChaosResistBoost.Value) __result = Plugin.ChaosResistBonus.Value;
                    else if (Plugin.GeneralResistBoost.Value) __result = Plugin.GeneralResistBonus.Value;
                    break;
                default:
                    if (Plugin.GeneralResistBoost.Value) __result = Plugin.GeneralResistBonus.Value;
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "get_PlayBattleParam")]
    internal static class Patch_BattleUnit_get_PlayBattleParam_Boosts
    {
        static void Postfix(BattleUnit __instance, ref BattleParameter __result)
        {
            if (!(__instance is Character)) return;
            if (Plugin.LifestealBoost.Value) __result.LifeSteal = Plugin.LifestealAmount.Value;
            if (Plugin.AddedDamageBoost.Value) __result.AdditionalDamage = Plugin.AddedDamageAmount.Value;
            if (Plugin.PenetrationBoost.Value) __result.Penetration = Plugin.PenetrationAmount.Value;
        }
    }

    [HarmonyPatch(typeof(BattleUnit), "get_PlayContentsBattleParam")]
    internal static class Patch_BattleUnit_get_PlayContentsBattleParam_Boosts
    {
        static void Postfix(BattleUnit __instance, ref BattleParameter __result)
        {
            if (!(__instance is Character)) return;
            if (Plugin.DefenseBoost.Value) __result.Defense *= Plugin.DefenseAmount.Value;
            if (Plugin.AttackBoost.Value) __result.Damage *= Plugin.AttackAmount.Value;
        }
    }
}