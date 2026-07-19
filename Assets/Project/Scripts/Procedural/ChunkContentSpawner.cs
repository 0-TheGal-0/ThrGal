using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    [Serializable]
    public class WeightedContentEntry {
        public GameObject prefab;
        [Tooltip("Базовый вес при нормальном Sanity.")]
        public float baseWeight = 1f;

        [Tooltip("Насколько сильнее выбирать этот вариант при низком Sanity " +
                 "(0 = не зависит от Sanity, больше 0 = 'неправильные' варианты вроде " +
                 "дерева с лицом или дома без двери). Пока не подключено — ISanityManager " +
                 "появится на этапе 6, см. GetSanityFactor().")]
        public float lowSanityBias = 0f;
    }

    // Регенерирует Layer 1 (Structures) чанка при переходе в Dark. Layer 0 (Skeleton —
    // рельеф, дороги) сюда не входит: он генерируется один раз на весь мир и не пересоздаётся,
    // это отдельная система вне ChunkContentSpawner.
    public class ChunkContentSpawner : MonoBehaviour {
        [SerializeField] private WeightedContentEntry[] weightedPrefabs;

        private IChunkManager _chunkManager;
        private IChunkAnchorRegistry _anchorRegistry;
        private ChunkSeedProvider _seedProvider;
        private ChunkObjectPool _pool;

        // Что сейчас заспавнено в чанке — нужно и чтобы вернуть старый инстанс в пул
        // перед подменой, и чтобы знать, из какого префаба он был создан (для Release).
        private readonly Dictionary<Vector2Int, (GameObject instance, GameObject prefab)> _spawnedContent = new();

        [Inject]
        public void Construct(IChunkManager chunkManager, IChunkAnchorRegistry anchorRegistry,
            ChunkSeedProvider seedProvider) {
            _chunkManager = chunkManager;
            _anchorRegistry = anchorRegistry;
            _seedProvider = seedProvider;
        }

        private void Awake() {
            _pool = new ChunkObjectPool(transform);
        }

        private void OnEnable() {
            _chunkManager.OnChunkStateChanged += HandleChunkStateChanged;
        }

        private void OnDisable() {
            _chunkManager.OnChunkStateChanged -= HandleChunkStateChanged;
        }

        private void HandleChunkStateChanged(Vector2Int coord, ChunkState newState) {
            if (newState != ChunkState.Dark) return;

            // Сюжетные чанки регенерации не подлежат — тело девочки/ключевые предметы
            // не должны исчезнуть или замениться placeholder'ом.
            if (_anchorRegistry.IsAnchor(coord)) return;

            RegenerateChunk(coord);
        }

        private void RegenerateChunk(Vector2Int coord) {
            if (weightedPrefabs == null || weightedPrefabs.Length == 0) {
                Debug.LogWarning("ChunkContentSpawner: пул weightedPrefabs пуст, регенерировать нечем.");
                return;
            }

            if (_spawnedContent.TryGetValue(coord, out var old) && old.instance != null) {
                _pool.Release(old.prefab, old.instance);
            }

            var random = _seedProvider.GetRandom(coord);
            var chosen = PickWeighted(random);

            Vector3 worldCenter = _chunkManager.ChunkToWorldCenter(coord);
            var instance = _pool.Get(chosen, worldCenter, Quaternion.identity, transform);
            _spawnedContent[coord] = (instance, chosen);
        }

        private GameObject PickWeighted(System.Random random) {
            // GetSanityFactor() пока всегда 0 — реального ISanityManager ещё нет (этап 6).
            // Когда появится, тут будет [0..1], где 1 = минимальный Sanity игрока.
            float sanityFactor = GetSanityFactor();

            float totalWeight = 0f;
            foreach (var entry in weightedPrefabs)
                totalWeight += entry.baseWeight + entry.lowSanityBias * sanityFactor;

            // System.Random.NextDouble(), не UnityEngine.Random — чтобы выбор был
            // воспроизводим по seed'у чанка, а не по глобальному состоянию UnityEngine.Random.
            double roll = random.NextDouble() * totalWeight;
            double cumulative = 0;

            foreach (var entry in weightedPrefabs) {
                cumulative += entry.baseWeight + entry.lowSanityBias * sanityFactor;
                if (roll <= cumulative)
                    return entry.prefab;
            }

            return weightedPrefabs[^1].prefab; // на случай ошибок округления float
        }

        // Заглушка до этапа 6. Сознательно не инжектю ISanityManager сейчас —
        // он ещё не зарегистрирован в LifetimeScope, а обязательная [Inject]-зависимость
        // на незарегистрированный интерфейс уронит контейнер при старте сцены.
        private float GetSanityFactor() => 0f;
    }
}