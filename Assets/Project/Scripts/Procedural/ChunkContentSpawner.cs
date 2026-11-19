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
        [Tooltip("Не выбирать тот же префаб, что стоял в чанке до этого. Без этого при трёх префабах " +
         "около 40% регенераций выглядят как «ничего не изменилось».")]
        [SerializeField] private bool avoidRepeatingPrevious = true;

        // Сколько раз чанк уже регенерировался. Пока живёт только в памяти;
        // на этапе 7 это значение нужно будет сохранять вместе с остальным состоянием мира.
        private readonly Dictionary<Vector2Int, int> _visitCounts = new();

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

            _visitCounts.TryGetValue(coord, out int visit);
            _visitCounts[coord] = visit + 1;

            GameObject previousPrefab = null;
            if (_spawnedContent.TryGetValue(coord, out var old)) {
                previousPrefab = old.prefab;
                if (old.instance != null)
                    _pool.Release(old.prefab, old.instance);
            }

            var random = _seedProvider.GetRandom(coord, visit);
            var chosen = PickWeighted(random, avoidRepeatingPrevious ? previousPrefab : null);


            Vector3 worldCenter = _chunkManager.ChunkToWorldCenter(coord);
            var instance = _pool.Get(chosen, worldCenter, Quaternion.identity, transform);
            _spawnedContent[coord] = (instance, chosen);
        }

        private GameObject PickWeighted(System.Random random, GameObject exclude) {
            float sanityFactor = GetSanityFactor();

            float totalWeight = 0f;
            foreach (var entry in weightedPrefabs) {
                if (entry.prefab == exclude) continue;
                totalWeight += entry.baseWeight + entry.lowSanityBias * sanityFactor;
            }
            // Исключать нечего (один вариант в пуле) — выбираем как обычно.
            if (totalWeight <= 0f && exclude != null)
                return PickWeighted(random, null);

            double roll = random.NextDouble() * totalWeight;
            double cumulative = 0;
            GameObject last = null;

            foreach (var entry in weightedPrefabs) {
                if (entry.prefab == exclude) continue;
                last = entry.prefab;
                cumulative += entry.baseWeight + entry.lowSanityBias * sanityFactor;
                if (roll <= cumulative)
                    return entry.prefab;
            }

            return last ?? weightedPrefabs[^1].prefab; // на случай ошибок округления float
        }

        // Заглушка до этапа 6. Сознательно не инжектю ISanityManager сейчас —
        // он ещё не зарегистрирован в LifetimeScope, а обязательная [Inject]-зависимость
        // на незарегистрированный интерфейс уронит контейнер при старте сцены.
        private float GetSanityFactor() => 0f;
    }
}