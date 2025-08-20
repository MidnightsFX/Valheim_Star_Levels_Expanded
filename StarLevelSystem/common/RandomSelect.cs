using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static StarLevelSystem.common.DataObjects;

namespace StarLevelSystem.common
{
    internal static class RandomSelect
    {
        public static string RandomSelectFromWeightedList(List<ProbabilityEntry> listOfWeights) {
            float totalweight = listOfWeights.Select(x => x.selectionWeight).Sum();
            if (totalweight == 0) { return "none"; }
            float selection = UnityEngine.Random.Range(0, totalweight);
            float current_weight = 0f;
            //Logger.LogDebug($"Total weight is {totalweight}, random selection is {selection}");
            foreach (var entry in listOfWeights) {
                current_weight += entry.selectionWeight;
                //Logger.LogDebug($"Current weight is {current_weight} >= {selection} for entry {entry.Name} - {entry.selectionWeight}");
                if (current_weight >= selection) {
                    Logger.LogDebug($"Randomly selected {entry.Name} with weight {entry.selectionWeight} from total {totalweight}");
                    return entry.Name;
                }
            }
            // Fallback, realistically this is never used.
            //Logger.LogWarning($"Failed to select a random entry from the list, returning a random entry instead.");
            return listOfWeights.ToArray()[UnityEngine.Random.Range(0, listOfWeights.Count - 1)].Name;
        }
    }
}
