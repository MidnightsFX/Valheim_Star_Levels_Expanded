using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StarLevelSystem.common
{
    public static class Extensions
    {
        static Random rand = new Random();
        public static void Times(this int count, Action action)
        {
            for (int i = 0; i < count; i++)
            {
                action();
            }
        }

        public static KeyValuePair<string, List<string>> RandomEntry(Dictionary<string, List<string>> dict, List<string> removedKeys = null) {
            List<string> keys = dict.Keys.ToList();
            if (removedKeys != null) {
                keys = keys.Where(k => !removedKeys.Contains(k)).ToList();
            }
            if (keys.Count == 0) {
                return new KeyValuePair<string, List<string>>(key: null, value: null);
            }
            string key = keys[UnityEngine.Random.Range(0, keys.Count - 1)];
            return new KeyValuePair<string, List<string>>(key: key, value: dict[key]);
        }
    }
}
