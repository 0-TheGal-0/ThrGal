using System.Collections.Generic;
using UnityEngine;

namespace Game.Procedural {
    // Простой пул по префабу-ключу. Addressables сюда сознательно не подключаем —
    // по договорённости добавим, когда контента реально станет много.
    public class ChunkObjectPool {
        private readonly Dictionary<GameObject, Queue<GameObject>> _pools = new();
        private readonly Transform _poolRoot;

        public ChunkObjectPool(Transform poolRoot) {
            _poolRoot = poolRoot;
        }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent) {
            if (!_pools.TryGetValue(prefab, out var queue))
                _pools[prefab] = queue = new Queue<GameObject>();

            GameObject instance;
            if (queue.Count > 0) {
                instance = queue.Dequeue();
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.transform.SetParent(parent);
                instance.SetActive(true);
            } else {
                instance = Object.Instantiate(prefab, position, rotation, parent);
            }
            return instance;
        }

        public void Release(GameObject prefab, GameObject instance) {
            instance.SetActive(false);
            instance.transform.SetParent(_poolRoot);

            if (!_pools.TryGetValue(prefab, out var queue))
                _pools[prefab] = queue = new Queue<GameObject>();

            queue.Enqueue(instance);
        }
    }
}