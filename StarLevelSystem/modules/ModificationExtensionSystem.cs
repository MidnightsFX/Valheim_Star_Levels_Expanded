using HarmonyLib;
using Jotunn.Managers;
using StarLevelSystem.Data;
using StarLevelSystem.Modifiers;
using StarLevelSystem.Modifiers.Control;
using StarLevelSystem.modules.Sizes;
using StarLevelSystem.modules.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static StarLevelSystem.common.DataObjects;
using static StarLevelSystem.Data.CreatureModifiersData;
using DamageType = StarLevelSystem.common.DataObjects.DamageType;

namespace StarLevelSystem.modules
{
    internal static class ModificationExtensionSystem
    {

        public static List<string> ForceLeveledCreatures = new List<string>();

        internal static void LeveledCreatureListChanged(object s, EventArgs e) {
            SetupForceLeveledCreatureList();
        }

        internal static void SetupForceLeveledCreatureList() {
            ForceLeveledCreatures.Clear();
            foreach (var item in ValConfig.SpawnsAlwaysControlled.Value.Split(','))
            {
                ForceLeveledCreatures.Add(item);
            }
        }

    }
}
