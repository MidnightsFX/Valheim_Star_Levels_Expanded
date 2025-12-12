using HarmonyLib;
using Jotunn;
using Jotunn.Extensions;
using Jotunn.Managers;
using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.modules
{
    public static class LevelSystem
    {
        public static Vector3 center = new Vector3(0, 0, 0);
        private static bool buildingMapRings = false;

        public static void SetAndUpdateCharacterLevel(Character character, int level) {
            if (character == null) { return; }
            character.m_level = level;
            character.SetupMaxHealth();
            if (character.m_nview != null && character.m_nview.GetZDO() != null) {
                character.m_nview.GetZDO().Set(ZDOVars.s_level, level);
            }
        }

        public static void SetCharacterLevelControl(Character chara, int providedLevel) {
            if (chara == null) { return; }
            if (ValConfig.ControlSpawnerLevels.Value) {
                ModificationExtensionSystem.CreatureSetup(chara, providedLevel);
                return;
            }
            // Fallback
            Logger.LogDebug($"Setting creature level from fallback provided {providedLevel}");
            chara.SetLevel(providedLevel);
        }

        public static int DetermineLevel(Character character, ZDO cZDO, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings, int leveloverride = 0)
        {
            if (character == null || cZDO == null) {
                Logger.LogWarning($"Creature null or nview null, cannot set level.");
                return 1;
            }
            if (leveloverride > 0) {
                //character.m_level = leveloverride;
                //character.SetLevel(leveloverride);
                return leveloverride;
            }

            int clevel = cZDO.GetInt(ZDOVars.s_level, 0);
            //Logger.LogDebug($"Current level from ZDO: {clevel}");
            if (clevel <= 0 || ValConfig.OverlevedCreaturesGetRerolledOnLoad.Value && clevel > ValConfig.MaxLevel.Value) {
                // Determine max level
                int max_level = ValConfig.MaxLevel.Value;
                int min_level = 0;
                if (biome_settings != null && biome_settings.BiomeMaxLevelOverride != 0) { max_level = biome_settings.BiomeMaxLevelOverride; }
                if (creature_settings != null && creature_settings.CreatureMaxLevelOverride > -1) { max_level = creature_settings.CreatureMaxLevelOverride; }
                
                if (biome_settings != null && biome_settings.BiomeMinLevelOverride > 0) { min_level = biome_settings.BiomeMinLevelOverride; }
                if (creature_settings != null && creature_settings.CreatureMinLevelOverride > -1) { min_level = creature_settings.CreatureMinLevelOverride; }
                min_level += 1;
                max_level += 1;

                float levelup_roll = UnityEngine.Random.Range(0f, 100f);
                Vector3 p = character.transform.position;
                float distance_from_center = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(center.x, center.z));
                float distance_level_modifier = 1;
                SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() { };
                SortedDictionary<int, float> levelup_chances = LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance;
                if (biome_settings != null) {
                    distance_level_modifier = biome_settings.DistanceScaleModifier;
                }
                // If we are using distance level bonuses | Check if we are in a distance level bonus area
                if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null)
                {
                    distance_levelup_bonuses = LevelSystem.SelectDistanceFromCenterLevelBonus(distance_from_center);
                }
                // Apply Night level scalers
                float nightScaleBonus = 1f;
                if (biome_settings != null && biome_settings.NightSettings != null && biome_settings.NightSettings.NightLevelUpChanceScaler != 1) {
                    nightScaleBonus = biome_settings.NightSettings.NightLevelUpChanceScaler;
                }
                if (creature_settings != null && creature_settings.NightSettings != null && creature_settings.NightSettings.NightLevelUpChanceScaler != 1) {
                    nightScaleBonus = creature_settings.NightSettings.NightLevelUpChanceScaler;
                }

                // Ensure we use character / biome specific levelup chances if those are set
                if (biome_settings != null && biome_settings.CustomCreatureLevelUpChance != null)
                {
                    levelup_chances = biome_settings.CustomCreatureLevelUpChance;
                }
                if (creature_settings != null && creature_settings.CustomCreatureLevelUpChance != null) {
                    levelup_chances = creature_settings.CustomCreatureLevelUpChance;
                }
                int level = LevelSystem.DetermineLevelRollResult(levelup_roll, max_level, levelup_chances, distance_levelup_bonuses, distance_level_modifier, nightScaleBonus);
                if (min_level > 0 && level < min_level) { level = min_level; }
                //Logger.LogDebug($"Determined level {level} min: {min_level} max {max_level}");
                //character.m_level = level;
                return level;
            }
            return clevel;
        }

        // For non-character levelups
        public static int DetermineLevel(GameObject creature, string creature_name, DataObjects.CreatureSpecificSetting creature_settings, BiomeSpecificSetting biome_settings, int maxLevel) {
            if (creature == null) {
                Logger.LogWarning($"Creature is null, cannot determine level, set 1.");
                return 1;
            }

            float levelup_roll = UnityEngine.Random.Range(0f, 100f);
            // Logger.LogDebug($"levelroll: {levelup_roll}");
            // Check if the creature has an override level
            // Use the default non-biom based levelup chances
            // Logger.LogDebug($"maxlevel default: {maxLevel}");
            maxLevel += 1;
            // Determine creature location to check its biome
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            float distance_from_center = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(center.x, center.z));
            SortedDictionary<int, float> distance_levelup_bonuses = new SortedDictionary<int, float>() {};
            SortedDictionary<int, float> levelup_chances = LevelSystemData.SLE_Level_Settings.DefaultCreatureLevelUpChance;
            if (levelup_chances == null) { levelup_chances = LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance; }

            // If we are using distance level bonuses | Check if we are in a distance level bonus area
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null) {
                distance_levelup_bonuses = SelectDistanceFromCenterLevelBonus(distance_from_center);
            }

            float distance_level_modifier = 1;
            if (biome_settings != null) { distance_level_modifier = biome_settings.DistanceScaleModifier; }
            if (creature_settings != null && creature_settings.DistanceScaleModifier != 1) { distance_level_modifier = creature_settings.DistanceScaleModifier; }
            // creature specific override
            if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration != null && LevelSystemData.SLE_Level_Settings.CreatureConfiguration.ContainsKey(creature_name)) {
                //Logger.LogDebug($"Creature specific config found for {creature_name}");
                if (creature_settings.CustomCreatureLevelUpChance != null) {
                    if (creature_settings.CreatureMaxLevelOverride > -1) { maxLevel = creature_settings.CreatureMaxLevelOverride; }
                    if (creature_settings.CustomCreatureLevelUpChance != null) { levelup_chances = creature_settings.CustomCreatureLevelUpChance; }
                    return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
                }
            }

            // biome override 
            if (biome_settings != null) {
                if (biome_settings.BiomeMaxLevelOverride > 0) { maxLevel = biome_settings.BiomeMaxLevelOverride; }
                if (biome_settings.CustomCreatureLevelUpChance != null) { levelup_chances = biome_settings.CustomCreatureLevelUpChance; }
                return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
            }
            return DetermineLevelRollResult(levelup_roll, maxLevel, levelup_chances, distance_levelup_bonuses, distance_level_modifier);
        }

        internal static SortedDictionary<int, float> SelectDistanceFromCenterLevelBonus(float distance_from_center) {

            //Logger.LogDebug($"Checking distance level bonus for distance {distance_from_center}");
            SortedDictionary<int, float> highest_selected_area = new SortedDictionary<int, float>() { };
            if (ValConfig.EnableDistanceLevelScalingBonus.Value && LevelSystemData.SLE_Level_Settings.DistanceLevelBonus != null) {
                // Check if we are in a distance level bonus area
                foreach (KeyValuePair<int, SortedDictionary<int, float>> kvp in LevelSystemData.SLE_Level_Settings.DistanceLevelBonus) {
                    //Logger.LogDebug($"Checking distance level area: {distance_from_center} >= {kvp.Key}");
                    if (distance_from_center >= kvp.Key) {
                        highest_selected_area = kvp.Value;
                    }
                    // Early return if we arn't going to find a larger bonus area
                    if (distance_from_center < kvp.Key) {
                        //Logger.LogDebug($"Distance Level area: {kvp.Key} bonuses: {string.Join(",", kvp.Value.Select(x => x.Value).ToList())}");
                        return highest_selected_area;
                    }
                }
                // This is the fallthrough for we are in the largest area available
                if (highest_selected_area.Count > 0) {
                    //Logger.LogDebug($"Distance Level area max: {string.Join(",", highest_selected_area.Select(x => x.Value).ToList())}");
                    return highest_selected_area;
                }
            }
            // No bonuses distance found
            return new SortedDictionary<int, float>() { };
        }

        // Consider decision tree for levelups to reduce iterations
        public static int DetermineLevelRollResult(float roll, int maxLevel, SortedDictionary<int, float> creature_levelup_chance, SortedDictionary<int, float> levelup_bonus, float distance_influence, float nightBonus = 1f) {
            // Do we want to do a re-roll after certain points?
            int selected_level = 0;
            //Logger.LogDebug($"Determine with: roll:{roll} maxlevel:{maxLevel} levelupchance:{creature_levelup_chance} levelup_bonus:{levelup_bonus} distance_influence:{distance_influence} nightbonus:{nightBonus}");
            creature_levelup_chance ??= LevelSystemData.DefaultConfiguration.DefaultCreatureLevelUpChance;

            //foreach (var lb in levelup_bonus) {
            //    Logger.LogDebug($"levelup bonus: {lb.Key} {lb.Value}");
            //}
            foreach (KeyValuePair<int, float> kvp in creature_levelup_chance) {
                //Logger.LogDebug($"levelup k: {kvp.Key} v: {kvp.Value}");
                if (levelup_bonus != null && levelup_bonus.ContainsKey(kvp.Key)) {
                    float distance_bonus = ((1f + levelup_bonus[kvp.Key]) * distance_influence);
                    float levelup_req = kvp.Value * nightBonus * distance_bonus;
                    //Logger.LogDebug($"Level Roll: {roll} >= {levelup_req} = {kvp.Value} * {nightBonus} * {distance_bonus}");
                    if (roll >= levelup_req || kvp.Key >= maxLevel) {
                        selected_level = kvp.Key;
                        //Logger.LogDebug($"Level Roll: {roll} >= {levelup_req} = {kvp.Value}(base) * {nightBonus}(Night) * {distance_bonus}(Distance) | Selected Level: {selected_level}");
                        break;
                    }
                } else {
                    if (roll >= (kvp.Value * nightBonus * distance_influence) || kvp.Key >= maxLevel) {
                        selected_level = kvp.Key;
                        //Logger.LogDebug($"Level Roll: {roll} >= {kvp.Value * nightBonus * distance_influence} = {kvp.Value}(base) * {nightBonus}(night) {distance_influence}(distance) || {kvp.Key} >= {maxLevel} | Selected Level: {selected_level}");
                        break;
                    }
                }
            }
            return selected_level;
        }

        public static void CreateLevelBonusRingMapOverlays()
        {
            if (ZNetScene.instance == null) { return; }
            if (ValConfig.EnableMapRingsForDistanceBonus.Value == false) { return; }
            Logger.LogDebug("Creating Level Bonus Rings on Map");
            if (buildingMapRings == false) {
                ZNetScene.instance.StartCoroutine(BuildMapRingOverlay());
            }
            buildingMapRings = true;
        }

        public static void OnRingCenterChanged(object s, EventArgs e)
        {
            SetRingCenter();
            CreateLevelBonusRingMapOverlays();
        }

        public static void SetRingCenter() {
            if (ValConfig.DistanceBonusIsFromStarterTemple.Value) {
                GameObject startTemple = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name == "StartTemple").FirstOrDefault();
                if (startTemple != null) {
                    center = startTemple.transform.position;
                } else {
                    Logger.LogWarning("Unable to find starter temple, bonus rings will use world center. (0,0,0)");
                    center = new Vector3(0, 0, 0);
                }
            } else {
                center = new Vector3(0, 0, 0);
            }
        }

        public static void UpdateMapColorSettingsOnChange(object s, EventArgs e)
        {
            Colorization.UpdateMapColorSelection();
            CreateLevelBonusRingMapOverlays();
        }

        public static void UpdateMapRingEnableSettingOnChange(object s, EventArgs e)
        {
            if (ValConfig.EnableMapRingsForDistanceBonus.Value)
            {
                ZNetScene.instance.StartCoroutine(BuildMapRingOverlay());
            } else {
                MinimapManager.MapOverlay ringbonuses = MinimapManager.Instance.GetMapOverlay("SLS-LevelBonus");
                ringbonuses.Enabled = false;
            }
        }



        public static IEnumerator BuildMapRingOverlay()
        {
            // Skip if distances are not defined.
            if (LevelSystemData.SLE_Level_Settings.DistanceLevelBonus == null || LevelSystemData.SLE_Level_Settings.DistanceLevelBonus.Keys.Count <= 0) {
                yield break;
            }
            if (ZNet.instance.IsDedicated()) {
                Logger.LogDebug("Server is headless, skipping minimap generation");
                yield break;
            }
            // Ensures that the previous ring overlay is removed first
            //MinimapManager.Instance.RemoveMapOverlay("SLS-LevelBonus");
            MinimapManager.MapOverlay ringbonuses = MinimapManager.Instance.GetMapOverlay("SLS-LevelBonus");
            ringbonuses.Enabled = true;

            // Create a Color array with space for every pixel of the map
            int mapSize = ringbonuses.TextureSize * ringbonuses.TextureSize;
            Color[] mainPixels = new Color[mapSize];

            // Clear the existing map?
            ringbonuses.OverlayTex.SetPixels(mainPixels);
            // Determine size of the world
            //float worlddiameter = WorldGenerator.worldSize * 2; // - to + range, we need the diameter
            // float meters_per_pixel = (Minimap.instance.m_textureSize / 2) + ValConfig.PixelMapOffsetRatio.Value; // ValConfig.PixelMapOffsetRatio.Value; // worlddiameter / ringbonuses.TextureSize; // 9.765625

            Minimap.instance.WorldToPixel(center, out int world_x, out int world_y);
            Logger.LogDebug($"Map centered: x:{world_x} y:{world_y}");

            int updates = 0;
            int levelring_color_index = 0;
            foreach (int ringDistance in LevelSystemData.SLE_Level_Settings.DistanceLevelBonus.Keys) {
                if (levelring_color_index >= Colorization.mapRingColors.Count) {
                    levelring_color_index = 0;
                }
                Color selectedColor = Colorization.mapRingColors[levelring_color_index];
                levelring_color_index++;

                int granularity = ringDistance * 10; // number of vertices per ring
                
                Vector3 radii = new Vector3(center.x + ringDistance, center.y, center.z);
                Minimap.instance.WorldToPixel(radii, out int radii_x, out int raddi_y);
                int map_radii = radii_x - world_x;
                Logger.LogDebug($"Set Ringsize: {ringDistance} -PixelMap-> {radii_x} | {map_radii}");
                //Vector2[] circle = new Vector2[granularity];
                float delta = (2 * Mathf.PI) / granularity;

                for (int i = 0; i < granularity; i++) {
                    // Ensure we do not overwhelm the system and get the task killed
                    updates++;
                    if (updates % 3_000 == 0) {
                        yield return new WaitForEndOfFrame();
                    }

                    float t = delta * i;
                    int x = Mathf.RoundToInt(world_x + Mathf.Cos(t) * map_radii);
                    int y = Mathf.RoundToInt(world_y + Mathf.Sin(t) * map_radii);
                    //circle[i] = new Vector2(x, y);
                    if (ringbonuses == null) { yield break; }

                    int index = (y * ringbonuses.TextureSize) + x;
                    // Index must be less than pixels due to zero indexing
                    if (index >= mainPixels.Length) {
                        continue;
                    }
                    //Logger.LogDebug($"Drawing ring for distance {ringDistance} pixels idx:{index} x:{x} y:{y}");
                    mainPixels[index] = selectedColor;
                }
            }

            if (ringbonuses == null) { yield break; }
            ringbonuses.OverlayTex.SetPixels(mainPixels);
            ringbonuses.OverlayTex.Apply();
            Logger.LogDebug("Finished Creating Level Bonus Rings on Minimap");
            buildingMapRings = false;
            yield break;
        }

        public static void UpdateMaxLevel() {
            IEnumerable<GameObject> fishes = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.name.StartsWith("Fish"));
            foreach (GameObject fish in fishes) {
                if (fish.GetComponent<Fish>() != null) {
                    //Logger.LogDebug($"Updating max quality {fish.gameObject.name}");
                    fish.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = 999;
                }
            }
        }

        public static int DeterministicDetermineTreeLevel(GameObject go)
        {
            Vector3 p = go.transform.position;
            float distance_from_center = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(center.x, center.z));
            int level = Mathf.RoundToInt(distance_from_center / (WorldGenerator.worldSize / ValConfig.TreeMaxLevel.Value));
            if (level < 1) { level = 1; }
            return level;
        }

        public static IEnumerator ModifyTreeWithLevel(TreeBase tree, int level)
        {
            yield return new WaitForSeconds(1f);
            if (tree == null) { yield break; }
            //Logger.LogDebug($"Tree level set to: {level}");
            float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * level);
            tree.m_health += (tree.m_health * 0.1f * level);
            // Logger.LogDebug($"Setting Tree size {scale}.");
            tree.transform.localScale *= scale;
            List<DropTable.DropData> drops = new List<DropTable.DropData>();
            foreach (var drop in tree.m_dropWhenDestroyed.m_drops)
            {
                DropTable.DropData lvlupdrop = new DropTable.DropData();
                // Scale the amount of drops based on level
                lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (1 + ValConfig.PerLevelTreeLootScale.Value * level));
                lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (1 + ValConfig.PerLevelTreeLootScale.Value * level));
                // Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                lvlupdrop.m_item = drop.m_item;
                drops.Add(lvlupdrop);
            }
            Physics.SyncTransforms();

            yield break;
        }

        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Awake))]
        public static class RandomTreeLevelExtension
        {
            public static void Postfix(TreeBase __instance)
            {
                if (ValConfig.EnableTreeScaling.Value == false) { return; }

                int storedLevel = 0;
                if (ValConfig.UseDeterministicTreeScaling.Value)
                {
                    storedLevel = CompositeLazyCache.GetOrAddCachedTreeEntry(__instance.m_nview);
                } else {
                    storedLevel = __instance.m_nview.GetZDO().GetInt(SLS_TREE, 0);
                    if (storedLevel == 0)
                    {
                        LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                        storedLevel = LevelSystem.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings, ValConfig.TreeMaxLevel.Value);
                        __instance.m_nview.GetZDO().Set(SLS_TREE, storedLevel);
                    }
                }
                if (storedLevel >= 1) {
                    __instance.StartCoroutine(LevelSystem.ModifyTreeWithLevel(__instance, storedLevel));
                }
            }
        }

        [HarmonyPatch(typeof(TreeLog))]
        public static class SetTreeLogPassLevel
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TreeLog.Destroy))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Call),
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Call),
                        new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(ZNetScene), nameof(ZNetScene.Destroy)))
                    ).RemoveInstructions(4)
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(TreeLog), nameof(TreeLog.m_subLogPrefab))),
                        new CodeMatch(OpCodes.Ldloc_S),
                        new CodeMatch(OpCodes.Callvirt)
                    ).Advance(1)
                    .RemoveInstructions(10)
                    .InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)10),
                        new CodeInstruction(OpCodes.Ldloc_S, (byte)11),
                        Transpilers.EmitDelegate(SetupTreeLog)
                    //).MatchStartForward(
                    //    new CodeMatch(OpCodes.Blt_S),
                    //    new CodeMatch(OpCodes.Ret)
                    //).Advance(1)
                    //.Insert(
                    //    new CodeInstruction(OpCodes.Ldarg_0),
                    //    Transpilers.EmitDelegate(RemoveTreeLogInst)
                    ).ThrowIfNotMatch("Unable to patch Tree Log Child Spawn Set Level.");

                return codeMatcher.Instructions();
            }

            [HarmonyPatch(nameof(TreeLog.Awake))]
            [HarmonyPostfix]
            static void SetupAwakeLog(TreeLog __instance) {
                int level = 1;
                if (ValConfig.UseDeterministicTreeScaling.Value) {
                    level = CompositeLazyCache.GetOrAddCachedTreeEntry(__instance.m_nview);
                } else {
                    if (__instance.m_nview.GetZDO() != null) {
                        //Logger.LogDebug("Checking stored Zvalue for tree level");
                        level = __instance.m_nview.GetZDO().GetInt(SLS_TREE, 1);
                    }
                }
                UpdateDrops(__instance, level);
                __instance.m_health += (__instance.m_health * 0.1f * level);
                __instance.GetComponent<ImpactEffect>()?.m_damages.Modify(1 + (0.1f * level));
            }

            [HarmonyPatch(nameof(TreeLog.Destroy))]
            [HarmonyPostfix]
            internal static void RemoveTreeLogInst(TreeLog __instance) {
                //Logger.LogDebug("Destroying Treelog");
                if (__instance.m_nview == null || __instance.m_nview.GetZDO() == null) { return; }
                CompositeLazyCache.RemoveTreeCacheEntry(__instance.m_nview.GetZDO().m_uid.ID);
                ZNetScene.instance.Destroy(__instance.gameObject);
            }

            internal static void SetupTreeLog(TreeLog instance, Transform tform, Quaternion qt)
            {
                GameObject go = GameObject.Instantiate(instance.m_subLogPrefab, tform.position, qt);
                ZNetView nview = go.GetComponent<ZNetView>();
                TreeLog tchild = go.GetComponent<TreeLog>();
                //Logger.LogDebug($"Setting Treelog scale {nview}");
                nview.SetLocalScale(instance.transform.localScale);
                // pass on the level
                // Logger.LogDebug($"Getting tree level");
                int level = 1;
                if (ValConfig.UseDeterministicTreeScaling.Value) {
                    level = CompositeLazyCache.GetOrAddCachedTreeEntry(instance.m_nview);
                } else {
                    if (instance.m_nview.GetZDO() != null)
                    {
                        //Logger.LogDebug("Checking stored Zvalue for tree level");
                        level = instance.m_nview.GetZDO().GetInt(SLS_TREE, 1);
                    }
                }

                //Logger.LogDebug($"Got Tree level {level}");
                UpdateDrops(tchild, level);
                tchild.m_health += (tchild.m_health * 0.1f * level);
                go.GetComponent<ImpactEffect>()?.m_damages.Modify(1 + (0.1f * level));
                //Logger.LogDebug($"Setting tree level {level}");
                nview.GetZDO().Set(SLS_TREE, level);
                // This is the last log point, destroy the parent
                ZNetScene.instance.Destroy(instance.gameObject);
            }

            internal static void UpdateDrops(TreeLog log, int level) {
                if (log.m_dropWhenDestroyed == null || log.m_dropWhenDestroyed.m_drops == null || level == 1) { return; }
                List<DropTable.DropData> drops = new List<DropTable.DropData>();
                // Update Drops
                Logger.LogDebug($"Updating Drops for tree to: {level}");
                foreach (var drop in log.m_dropWhenDestroyed.m_drops) {
                    DropTable.DropData lvlupdrop = new DropTable.DropData();
                    // Scale the amount of drops based on level
                    lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (ValConfig.PerLevelTreeLootScale.Value * level));
                    lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (ValConfig.PerLevelTreeLootScale.Value * level));
                    Logger.LogDebug($"Scaling drop {drop.m_item} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {level}.");
                    if (drop.m_item == null) { continue; }
                    lvlupdrop.m_item = drop.m_item;
                    drops.Add(lvlupdrop);
                }
                log.m_dropWhenDestroyed.m_drops = drops;
            }
        }

        public static void UpdateTreeSizeOnConfigChange(object s, EventArgs e)
        {
            ZNetScene.instance.StartCoroutine(UpdateAllTreeSizesOnConfigChangeCoroutine());
        }

        public static void UpdateBirdSizeOnConfigChange(object s, EventArgs e)
        {
            ZNetScene.instance.StartCoroutine(UpdateAllBirdSizesOnConfigChangeCoroutine());
        }

        public static void UpdateFishSizeOnConfigChange(object s, EventArgs e)
        {
            ZNetScene.instance.StartCoroutine(UpdateAllFishOnConfigChangeCoroutine());
        }

        public static IEnumerator UpdateAllTreeSizesOnConfigChangeCoroutine()
        {
            int updated = 0;
            IEnumerable<GameObject> trees = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<TreeBase>() != null);
            foreach (GameObject tree in trees)
            {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0) {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                TreeBase treebase = tree.GetComponent<TreeBase>();
                if (treebase == null || treebase.m_nview == null || treebase.m_nview.GetZDO() == null) { continue; }
                string treename = Utils.GetPrefabName(tree.gameObject);
                // Check scalar objects, or fall back to the reference prefab scale
                Vector3 baseSize = treebase.m_nview.GetZDO().GetVec3(ZDOVars.s_scaleHash, Vector3.zero);
                if (baseSize == Vector3.zero) {
                    float scaler = treebase.m_nview.GetZDO().GetFloat(ZDOVars.s_scaleScalarHash, 0f);
                    baseSize = new Vector3(scaler, scaler, scaler);
                }
                // Falling back to the reference prefab scale will set tree size to be uniform, which will likely be adjusted when reloaded
                if (baseSize == Vector3.zero) {
                    baseSize = PrefabManager.Instance.GetPrefab(treename).gameObject.transform.localScale;
                }
                if (ValConfig.EnableTreeScaling.Value == false) {
                    treebase.transform.localScale = baseSize;
                    continue;
                }

                if (ValConfig.UseDeterministicTreeScaling.Value)
                {
                    float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * CompositeLazyCache.GetOrAddCachedTreeEntry(treebase.m_nview));
                    treebase.transform.localScale = baseSize * scale;
                }
                else
                {
                    int storedLevel = treebase.m_nview.GetZDO().GetInt(SLS_TREE, 0);
                    if (storedLevel > 1)
                    {
                        float scale = 1 + (ValConfig.TreeSizeScalePerLevel.Value * storedLevel);
                        //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                        treebase.transform.localScale = baseSize * scale;
                    }
                }
            }
            yield break;
        }

        public static IEnumerator UpdateAllBirdSizesOnConfigChangeCoroutine()
        {
            int updated = 0;
            Dictionary<string, Vector3> BirdSizeReferences = new Dictionary<string, Vector3>();
            IEnumerable<GameObject> birds = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<RandomFlyingBird>() != null);
            foreach (GameObject bird in birds)
            {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0)
                {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                RandomFlyingBird treebase = bird.GetComponent<RandomFlyingBird>();
                if (treebase == null || treebase.m_nview == null || treebase.m_nview.GetZDO() == null) { continue; }
                string birdname = Utils.GetPrefabName(bird.gameObject);
                if (BirdSizeReferences.ContainsKey(birdname) == false)
                {
                    BirdSizeReferences.Add(birdname, PrefabManager.Instance.GetPrefab(birdname).gameObject.transform.localScale);
                }
                if (ValConfig.EnableScalingBirds.Value == false)
                {
                    treebase.transform.localScale = BirdSizeReferences[birdname];
                    continue;
                }

                int storedLevel = treebase.m_nview.GetZDO().GetInt(SLS_BIRD, 0);
                if (storedLevel > 1)
                {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    treebase.transform.localScale = BirdSizeReferences[birdname] * scale;
                }
            }
            yield break;
        }

        public static IEnumerator UpdateAllFishOnConfigChangeCoroutine()
        {
            int updated = 0;
            Dictionary<string, Vector3> FishSizeReference = new Dictionary<string, Vector3>();
            IEnumerable<GameObject> loadedFish = Resources.FindObjectsOfTypeAll<GameObject>().Where(obj => obj.GetComponent<Fish>() != null);
            foreach (GameObject fish in loadedFish)
            {
                updated++;
                if (updated % ValConfig.NumberOfCacheUpdatesPerFrame.Value == 0)
                {
                    yield return new WaitForEndOfFrame();
                    Physics.SyncTransforms();
                }
                Fish fishComp = fish.GetComponent<Fish>();
                if (fishComp == null || fishComp.m_nview == null || fishComp.m_nview.GetZDO() == null) { continue; }
                string fishname = Utils.GetPrefabName(fish.gameObject);
                if (FishSizeReference.ContainsKey(fishname) == false)
                {
                    FishSizeReference.Add(fishname, PrefabManager.Instance.GetPrefab(fishname).gameObject.transform.localScale);
                }
                if (ValConfig.EnableScalingFish.Value == false)
                {
                    fishComp.transform.localScale = FishSizeReference[fishname];
                    continue;
                }

                int storedLevel = fishComp.m_nview.GetZDO().GetInt(SLS_FISH, 0);
                if (storedLevel > 1)
                {
                    float scale = 1 + (ValConfig.FishSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    fishComp.transform.localScale = FishSizeReference[fishname] * scale;
                    continue;
                }
                ItemDrop id = fish.GetComponent<ItemDrop>();
                if (id.m_itemData.m_quality > 1)
                {
                    float scale = 1 + (ValConfig.FishSizeScalePerLevel.Value * id.m_itemData.m_quality);
                    //Logger.LogDebug($"Updating tree size {scale} for {tree.name}.");
                    fishComp.transform.localScale = FishSizeReference[fishname] * scale;
                    id.m_itemData.m_shared.m_scaleByQuality = ValConfig.FishSizeScalePerLevel.Value;
                    id.Save();
                }
            }
            yield break;
        }


        [HarmonyPatch(typeof(RandomFlyingBird), nameof(RandomFlyingBird.Awake))]
        public static class RandomFlyingBirdExtension
        {
            public static void Postfix(RandomFlyingBird __instance)
            {
                if (ValConfig.EnableScalingBirds.Value == false) { return; }
                int storedLevel = __instance.m_nview.GetZDO().GetInt(SLS_BIRD, 0);
                if (storedLevel == 0)
                {
                    LevelSystem.SelectCreatureBiomeSettings(__instance.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                    storedLevel = LevelSystem.DetermineLevel(__instance.gameObject, creature_name, creature_settings, biome_settings, ValConfig.BirdMaxLevel.Value);
                    __instance.m_nview.GetZDO().Set(SLS_BIRD, storedLevel);
                }
                if (storedLevel > 1)
                {
                    float scale = 1 + (ValConfig.BirdSizeScalePerLevel.Value * storedLevel);
                    //Logger.LogDebug($"Setting bird size {scale}.");
                    __instance.transform.localScale *= scale;
                    Physics.SyncTransforms();
                    DropOnDestroyed dropondeath = __instance.gameObject.GetComponent<DropOnDestroyed>();
                    List<DropTable.DropData> drops = new List<DropTable.DropData>();
                    foreach (var drop in dropondeath.m_dropWhenDestroyed.m_drops)
                    {
                        DropTable.DropData lvlupdrop = new DropTable.DropData();
                        // Scale the amount of drops based on level
                        lvlupdrop.m_stackMin = Mathf.RoundToInt(drop.m_stackMin * (1+ ValConfig.PerLevelBirdLootScale.Value * storedLevel));
                        lvlupdrop.m_stackMax = Mathf.RoundToInt(drop.m_stackMax * (1+ ValConfig.PerLevelBirdLootScale.Value * storedLevel));
                        //Logger.LogDebug($"Scaling drop {drop.m_item.name} from {drop.m_stackMin}-{drop.m_stackMax} to {lvlupdrop.m_stackMin}-{lvlupdrop.m_stackMax} for level {storedLevel}.");
                        lvlupdrop.m_item = drop.m_item;
                        drops.Add(lvlupdrop);
                    }
                    dropondeath.m_dropWhenDestroyed.m_drops = drops;
                }
            }
        }

        [HarmonyPatch(typeof(Growup))]
        public static class SetGrowUpLevel
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(Growup.GrowUpdate))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(false,
                    //new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.GetLevel))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                    ).RemoveInstructions(2).InsertAndAdvance(
                    Transpilers.EmitDelegate(SetupGrownUp)
                    ).ThrowIfNotMatch("Unable to patch child grow up level set.");

                return codeMatcher.Instructions();
            }

            internal static void SetupGrownUp(Character grownup, Character childChar)
            {
                CharacterCacheEntry cdc_child = CompositeLazyCache.GetCacheEntry(childChar);
                if (cdc_child == null)
                {
                    grownup.SetLevel(childChar.m_level);
                    return;
                } else {
                    CompositeLazyCache.UpdateCharacterCacheEntry(grownup, cdc_child);
                }
                ModificationExtensionSystem.CreatureSetup(grownup, multiply: false);
            }
        }


        [HarmonyPatch(typeof(Procreation))]
        public static class SetChildLevel
        {
            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(Procreation.Procreate))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchForward(true,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Tameable), nameof(Tameable.IsTamed))),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetTamed)))
                    ).RemoveInstructions(15).InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc, (byte)6),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    Transpilers.EmitDelegate(SetupChildCharacter)
                    ).ThrowIfNotMatch("Unable to patch child spawn level set.");

                return codeMatcher.Instructions();
            }

            internal static void SetupChildCharacter(Character chara, Procreation proc)
            {
                Logger.LogDebug($"Setting child level for {chara.m_name}");
                chara.SetTamed(true);

                if (ValConfig.RandomizeTameChildrenModifiers.Value == false && proc.m_character != null) {
                    CharacterCacheEntry cdc_parent = CompositeLazyCache.GetCacheEntry(proc.m_character);
                    if (cdc_parent == null)
                    {
                        chara.SetLevel(proc.m_character.m_level);
                        return;
                    }
                    // TODO: Add randomization, limits and variations to children
                    ModificationExtensionSystem.CreatureSetup(chara, proc.m_character.GetLevel());
                }

                int inheritedLevel = proc.m_character ? proc.m_character.GetLevel(): proc.m_minOffspringLevel;
                if (ValConfig.RandomizeTameChildrenLevels.Value == true)
                {
                    int level = UnityEngine.Random.Range(1, inheritedLevel);
                    Logger.LogDebug($"Character randomized level {level} (1-{inheritedLevel}) being used for child.");
                    ModificationExtensionSystem.CreatureSetup(chara, level);
                } else {
                    Logger.LogDebug($"Parent level {inheritedLevel} being used for child.");
                    ModificationExtensionSystem.CreatureSetup(chara, inheritedLevel);
                }
                if (ValConfig.SpawnMultiplicationAppliesToTames.Value == false && chara.m_nview.GetZDO() != null)
                {
                    Logger.LogDebug("Disabling spawn multiplier for tamed child.");
                    chara.m_nview.GetZDO().Set(SLS_SPAWN_MULT, true);
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetLevelDamageFactor))]
        public static class SetupMaxLevelDamagePatch {
            public static bool Prefix(Attack __instance, ref float __result) {
                // We do not want to skip to return 1 if the character is not leveled because this might need to apply base damage changes too
                if (__instance.m_character == null || __instance.m_character.m_nview == null || __instance.m_character.m_nview.GetZDO() == null) {
                    __result = 1f;
                    return false;
                }
                __result = __instance.m_character.m_nview.GetZDO().GetFloat(SLS_DAMAGE_MODIFIER, 1);
                //Logger.LogDebug($"Damage Level Factor: {__result}");
                return false;
            }
        }

        public static void SelectCreatureBiomeSettings(GameObject creature, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome creature_biome) {
            // Determine creature max level from biome
            Vector3 p = creature.transform.position;
            creature_name = Utils.GetPrefabName(creature.gameObject);
            Heightmap.Biome biome = Heightmap.FindBiome(p);
            creature_biome = biome;
            biome_settings = null;
            // TODO: Make trees less complex
            //Logger.LogDebug($"{creature_name} {biome} {p}");

            if (LevelSystemData.SLE_Level_Settings.BiomeConfiguration != null) {
                bool biome_all_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(Heightmap.Biome.All, out var allBiomeConfig);
                if (biome_all_setting_check)
                {
                    biome_settings = allBiomeConfig;
                }
                //Logger.LogDebug($"Biome all config checked");
                bool biome_setting_check = LevelSystemData.SLE_Level_Settings.BiomeConfiguration.TryGetValue(biome, out var biomeConfig);
                if (biome_setting_check && biome_all_setting_check)
                {
                    biome_settings = Extensions.MergeBiomeConfigs(biomeConfig, allBiomeConfig);
                }
                else if (biome_setting_check)
                {
                    biome_settings = biomeConfig;
                }
                //Logger.LogDebug($"Merged biome configs");
            }

            creature_settings = null;
            if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration != null) {
                if (LevelSystemData.SLE_Level_Settings.CreatureConfiguration.TryGetValue(creature_name, out var creatureConfig)) { creature_settings = creatureConfig; }
                //Logger.LogDebug($"Set character specific configs");
            }
        }

        [HarmonyPatch(typeof(CreatureSpawner))]
        public static class CreatureSpawnerSpawn
        {
            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(CreatureSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(CreatureSpawnerCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .Insert(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .MatchStartBackwards(
                    new CodeMatch(OpCodes.Stloc_S),
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .ThrowIfNotMatch("Unable to patch CreatureSpawner.Spawn.");

                return codeMatcher.Instructions();
            }

            private static void CreatureSpawnerCharacterLevelControl(Character chara, int providedLevel)
            {
                //Logger.LogDebug($"CreatureSpawner.Spawn setting {chara.m_name} {providedLevel}");
                SetCharacterLevelControl(chara, providedLevel);
            }

            [HarmonyPatch(nameof(CreatureSpawner.Awake))]
            [HarmonyPostfix]
            public static void Postfix(CreatureSpawner __instance)
            {
                __instance.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(SpawnArea))]
        public static class SpawnAreaSpawnOnePatch
        {
            //[HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnArea.SpawnOne))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(GameObject), "GetComponent", null, new Type[] { typeof(Character) })),
                    new CodeMatch(OpCodes.Stloc_S)
                    )
                .Advance(2)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_S, 5),
                    new CodeInstruction(OpCodes.Ldloc_2),
                    Transpilers.EmitDelegate(SpawnAreaSetCharacterLevelControl)
                    )
                .CreateLabelOffset(out Label label, offset: 28)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Br, label))
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), "GetCenterPoint")),
                    new CodeMatch(OpCodes.Stloc_S)
                    )
                .ThrowIfNotMatch("Unable to patch SpawnArea.SpawnOne.");

                return codeMatcher.Instructions();
            }

            private static void SpawnAreaSetCharacterLevelControl(Character chara, SpawnArea.SpawnData spawndata) {
                int fallback_level = spawndata != null ? spawndata.m_minLevel : 1;
                //Logger.LogDebug($"SpawnArea.SpawnOne setting {chara.m_name} {fallback_level}");
                SetCharacterLevelControl(chara, fallback_level);
            }

            [HarmonyPatch(nameof(SpawnArea.SelectWeightedPrefab))]
            [HarmonyPostfix]
            public static void Prefix(ref SpawnArea.SpawnData __result)
            {
                if (__result == null) { return; }
                __result.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(SpawnSystem))]
        public static class SpawnSystemSpawnPatch
        {
            [HarmonyEmitIL(".dumps")]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnSystem.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(SpawnSystem.SpawnData), nameof(SpawnSystem.SpawnData.m_prefab))),
                    new CodeMatch(OpCodes.Ldarg_2),
                    new CodeMatch(OpCodes.Call),
                    new CodeMatch(OpCodes.Call),
                    new CodeMatch(OpCodes.Stloc_0)
                )
                .Advance(5)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldloc_0),
                    Transpilers.EmitDelegate(SpawnSystemSetCharacterWithoutZoneLimits)
                )
                .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SpawnSystemSetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(ItemDrop), nameof(ItemDrop.SetQuality)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(SetItemLevelFish)
                )
                .ThrowIfNotMatch("Unable to patch SpawnSystem Spawn set level.");

                return codeMatcher.Instructions();
            }

            private static void SpawnSystemSetCharacterWithoutZoneLimits(GameObject go)
            {
                Character chara = go.GetComponent<Character>();
                if (chara == null) { return; }
                //Logger.LogDebug($"SpawnSystem.Spawn setting without zone control {chara.m_name}");
                SetCharacterLevelControl(chara, 1);
            }

            private static void SetItemLevelFish(ItemDrop item, int _providedLevel)
            {
                LevelSystem.SelectCreatureBiomeSettings(item.gameObject, out string creature_name, out DataObjects.CreatureSpecificSetting creature_settings, out BiomeSpecificSetting biome_settings, out Heightmap.Biome biome);
                int determinedLevel = LevelSystem.DetermineLevel(item.gameObject, creature_name, creature_settings, biome_settings, ValConfig.FishMaxLevel.Value);
                // not sure we need max quality set high
                item.m_itemData.m_shared.m_maxQuality = 999;
                item.SetQuality(determinedLevel);
                item.m_itemData.m_shared.m_scaleByQuality = ValConfig.FishSizeScalePerLevel.Value;
                item.Save();
            }

            private static void SpawnSystemSetCharacterLevelControl(Character chara, int providedLevel)
            {
                //Logger.LogDebug($"SpawnSystem.Spawn setting {chara.m_name} {providedLevel}");
                SetCharacterLevelControl(chara, providedLevel);
            }

            [HarmonyPatch(nameof(SpawnSystem.Spawn))]
            [HarmonyPrefix]
            public static void Prefix(ref SpawnSystem.SpawnData critter) {
                critter.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(TriggerSpawner))]
        public static class TriggerSpawnerSpawnPatch
        {
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TriggerSpawner.Spawn))]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codeMatcher = new CodeMatcher(instructions, generator);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(TriggerSpawnerSetCharacterLevelControl)
                )
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(1)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .MatchStartBackwards(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(TriggerSpawner), nameof(TriggerSpawner.m_maxLevel))),
                    new CodeMatch(OpCodes.Ldc_I4_1)
                )
                .Advance(2)
                .RemoveInstructions(1)
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4, 0))
                .ThrowIfNotMatch("Unable to patch TriggerSpawner.Spawn.");

                return codeMatcher.Instructions();
            }

            private static void TriggerSpawnerSetCharacterLevelControl(Character chara, int providedLevel)
            {
                //Logger.LogDebug($"TriggerSpawner.Spawn setting {chara.m_name} {providedLevel}");
                SetCharacterLevelControl(chara, providedLevel);
            }

            [HarmonyPatch(nameof(TriggerSpawner.Awake))]
            [HarmonyPostfix]
            public static void Postfix(TriggerSpawner __instance)
            {
                __instance.m_minLevel = 1;
            }
        }

        [HarmonyPatch(typeof(SpawnAbility))]
        public static class SpawnAbilitySpawnPatch
        {
            // Note: This is an IEnumerator so we need to patch the MoveNext method inside the generated class

            //[HarmonyDebug]
            [HarmonyTranspiler]
            [HarmonyPatch(nameof(SpawnAbility.Spawn), MethodType.Enumerator)]
            static IEnumerable<CodeInstruction> TranspileMoveNext(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Character), nameof(Character.SetLevel)))
                    ).RemoveInstructions(1).InsertAndAdvance(
                    Transpilers.EmitDelegate(SetSpawnAbilityLevelControl)
                    ).ThrowIfNotMatch("Unable to patch TriggerSpawner Spawn set level.");

                return codeMatcher.Instructions();
            }

            public static void SetSpawnAbilityLevelControl(Character chara, int providedLevel)
            {
                if (ValConfig.ControlAbilitySpawnedCreatures.Value)
                {
                    ModificationExtensionSystem.CreatureSetup(chara, providedLevel);
                    return;
                }
                // Fallback
                chara.SetLevel(providedLevel);
            }
        }

        // Add support for Seeker egg hatches
        [HarmonyPatch(typeof(EggHatch))]
        public static class EggHatchSpawnPatch
        {
            // Skip original prefix that causes levelup to trigger for seeker broods
            [HarmonyPatch(nameof(EggHatch.Hatch))]
            static bool Prefix(EggHatch __instance) {
                __instance.m_hatchEffect.Create(__instance.transform.position, __instance.transform.rotation, null, 1f, -1);
                GameObject go = UnityEngine.Object.Instantiate(__instance.m_spawnPrefab, __instance.transform.TransformPoint(__instance.m_spawnOffset), Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));
                Character chara = go.GetComponent<Character>();
                if (chara != null) {
                    ModificationExtensionSystem.CreatureSetup(chara);
                }
                __instance.m_nview.Destroy();
                return false;
            }
        }

        // Ensure creatures that are spawned as loot drops also get leveled
        [HarmonyPatch(typeof(ItemDrop))]
        public static class DropOnDestroyedSpawnPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(ItemDrop.OnCreateNew), new Type[] { typeof(GameObject) } )]
            static void Postfix(GameObject go) {
                Character chara = go.GetComponent<Character>();
                if (chara != null) {
                    ModificationExtensionSystem.CreatureSetup(chara);
                }
            }
        }

    }
}
