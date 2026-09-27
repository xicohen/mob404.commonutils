using UnityEngine;

namespace Mob404.Common
{
    public abstract class Singleton<T> where T : new()
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new T();
                }
                return _instance;
            }
        }
    }

    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : Component
    {
        public static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("GameObject");
                    go.name = typeof(T).ToString();
                    _instance = go.AddComponent<T>();
                    GameObject.DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
    }
}
