using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static StarLevelSystem.Data.CreatureModifiersData;

namespace StarLevelSystem.common
{
    public static class Extensions
    {
        public static void Times(this int count, Action action)
        {
            for (int i = 0; i < count; i++)
            {
                action();
            }
        }

        public static KeyValuePair<ModifierNames, List<string>> RandomEntry(Dictionary<ModifierNames, List<string>> dict, List<ModifierNames> removedKeys = null) {
            List<ModifierNames> keys = dict.Keys.ToList();
            if (removedKeys != null) {
                keys = keys.Where(k => !removedKeys.Contains(k)).ToList();
            }
            if (keys.Count == 0) {
                return new KeyValuePair<ModifierNames, List<string>>(key: ModifierNames.None, value: null);
            }
            ModifierNames key = keys[UnityEngine.Random.Range(0, keys.Count - 1)];
            return new KeyValuePair<ModifierNames, List<string>>(key: key, value: dict[key]);
        }

        public static List<Character> GetCharactersInRange(Vector3 position, float range)
        {
            Collider[] objs_near = Physics.OverlapSphere(position, range);
            List <Character> characters = new List<Character>();

            foreach (var col in objs_near) {
                var chara = col.GetComponentInChildren<Character>();
                if (chara != null) { characters.Add(chara); }
            }

            return characters;
        }
    }
}
