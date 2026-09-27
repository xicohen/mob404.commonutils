using System.Collections.Generic;
using UnityEngine;

namespace Mob404.Common
{
    // Cache WaitForSeconds theo thoi luong de khoi new moi lan yield trong coroutine.
    public class Helpers
    {
        private static readonly Dictionary<float, WaitForSeconds> WaitDictionary = new Dictionary<float, WaitForSeconds>();

        public static WaitForSeconds GetWait(float time)
        {
            if (WaitDictionary.TryGetValue(time, out var wait)) return wait;
            WaitDictionary[time] = new WaitForSeconds(time);
            return WaitDictionary[time];
        }
    }
}
