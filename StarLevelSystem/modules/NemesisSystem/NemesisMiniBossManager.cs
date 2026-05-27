using StarLevelSystem.common;
using StarLevelSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace StarLevelSystem.modules.NemesisSystem {
    internal static class NemesisMiniBossManager {
        internal static int NumNemesisNames = 9;
        internal static int NumNemesisPrefixes = 6;
        internal static int NumNemesisPostfixes = 4;



        // This builds a localizable boss name using prefix, postfix
        public static string RandomlySelectBossName(string playername, bool include_postfix = true, bool include_prefix = true) {
            StringBuilder sb = new StringBuilder();
            // Small chance to add a prefix to the creatures name
            if (include_prefix && UnityEngine.Random.Range(0, 1f) < 0.10f) {
                int prefix_idx = UnityEngine.Random.Range(1, NumNemesisPrefixes);
                sb.Append("$SLS_Miniboss_Prefix" + prefix_idx + " ");
            }
            
            int name_idx = UnityEngine.Random.Range(1, NumNemesisNames);
            sb.Append("$SLS_Miniboss_Name" + name_idx);

            // Chance to include the player name reference
            if (include_postfix && UnityEngine.Random.Range(0,1f) < 0.25f) {
                int postfix_idx = UnityEngine.Random.Range(1, NumNemesisPostfixes);
                sb.Append(" $SLS_Miniboss_postfix" + postfix_idx + " " + playername);
            }
            return sb.ToString();
        }

        public static DataObjects.NemesisMiniboss RandomlySelectAppropriateMiniboss(Vector3 pos) {
            Heightmap.Biome targetBiome = Heightmap.FindBiome(pos);
            
            foreach(DataObjects.NemesisMiniboss miniboss in SLSExtensions.ShuffleList(NemesisSystemData.SLE_Nemesis_Settings.AvailableMiniBosses)) {
                if (miniboss.Biome != targetBiome) { continue; }

                return miniboss;
            }
            return null;
        }
    }
}
