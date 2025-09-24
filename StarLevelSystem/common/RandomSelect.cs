using StarLevelSystem.modules;
using System.Collections.Generic;
using System.Linq;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    internal static class RandomSelect
    {
        public static string RandomSelectFromWeightedListWithExclusions(List<ProbabilityEntry> listOfWeights, List<string> exclude) {
            List<ProbabilityEntry> possibleModifiers = listOfWeights.Where(x => exclude.Contains(x.Name) == false).ToList();
            float totalweight = possibleModifiers.Select(x => x.SelectionWeight).Sum();
            if (totalweight == 0) { return CreatureModifiers.NoMods; }
            float selection = UnityEngine.Random.Range(0, totalweight);
            float current_weight = 0f;
            //Logger.LogDebug($"Total weight is {totalweight}, random selection is {selection}");
            foreach (var entry in listOfWeights) {
                current_weight += entry.SelectionWeight;
                //Logger.LogDebug($"Current weight is {current_weight} >= {selection} for entry {entry.Name} - {entry.SelectionWeight}");
                if (current_weight >= selection) {
                    //Logger.LogDebug($"Randomly selected {entry.Name}");
                    return entry.Name;
                }
            }
            // Fallback, realistically this is never used.
            // Logger.LogWarning($"Failed to select a random entry from the list, returning a random entry instead.");
            return possibleModifiers.ToArray()[UnityEngine.Random.Range(0, listOfWeights.Count - 1)].Name;
        }
    }
}
