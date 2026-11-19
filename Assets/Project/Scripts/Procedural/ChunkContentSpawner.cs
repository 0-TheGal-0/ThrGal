using System.Collections.Generic;
using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    // Регенерирует Layer 1 (Structures) чанка при переходе в Dark. Layer 0 (Skeleton —
    // рельеф, дороги) сюда не входит: он статичен и запечён в ChunkMapData.
    public class ChunkContentSpawner : MonoBehaviour {
        [Header("Размещение")]
        [Tooltip("Отступ от границы чанка (м), чтобы объект не оказался на стыке с соседним чанком.")]
        [SerializeField, Min(0f)] private float edgeMargin = 1f;

        [Tooltip("Сколько раз пробовать найти подходящую точку для одного объекта, прежде чем пропустить его.")]
        [SerializeField, Min(1)] private int maxPlacementAttempts = 8;

        private IChunkManager _chunkManager;
        private IChunkAnchorRegistry _anchorRegistry;
        private ChunkSeedProvider _seedProvider;
        private BiomeMap _biomeMap;
        private ChunkObjectPool _pool;
        private Terrain _terrain;

        // Сколько раз чанк уже регенерировался. Пока живёт только в памяти;
        // на этапе 7 это значение нужно будет сохранять вместе с остальным состоянием мира.
        private readonly Dictionary<Vector2Int, int> _visitCounts = new();

        // Что сейчас стоит в чанке: инстанс + префаб, из которого он создан (для Release в пул).
        private readonly Dictionary<Vector2Int, List<(GameObject instance, GameObject prefab)>> _spawnedContent = new();

        [Inject]
        public void Construct(IChunkManager chunkManager, IChunkAnchorRegistry anchorRegistry,
            ChunkSeedProvider seedProvider, BiomeMap biomeMap) {
            _chunkManager = chunkManager;
            _anchorRegistry = anchorRegistry;
            _seedProvider = seedProvider;
            _biomeMap = biomeMap;
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

            // Сюжетные чанки регенерации не подлежат.
            if (_anchorRegistry.IsAnchor(coord)) return;

            RegenerateChunk(coord);
        }

        private void RegenerateChunk(Vector2Int coord) {
            _visitCounts.TryGetValue(coord, out int visit);
            _visitCounts[coord] = visit + 1;

            ReleaseChunkContent(coord);

            if (_terrain == null) _terrain = Terrain.activeTerrain;
            if (_terrain == null) {
                Debug.LogWarning("ChunkContentSpawner: на сцене нет активного Terrain, размещать не на чем.");
                return;
            }

            if (!_biomeMap.TryGetDefinition(coord, out var biome)) {
                Debug.LogWarning($"ChunkContentSpawner: у чанка {coord} нет биома или для его id нет BiomeDefinition.");
                return;
            }
            if (biome.Entries.Count == 0) return;

            // Все случайные числа этого чанка идут из одного генератора в фиксированном порядке:
            // (seed, coord, visit) -> один и тот же результат.
            var random = _seedProvider.GetRandom(coord, visit);
            int count = random.Next(biome.MinObjectsPerChunk, biome.MaxObjectsPerChunk + 1);
            if (count <= 0) return;

            if (!_spawnedContent.TryGetValue(coord, out var list))
                _spawnedContent[coord] = list = new List<(GameObject, GameObject)>();

            for (int i = 0; i < count; i++)
                TryPlaceOne(coord, biome, random, list);
        }

        private void ReleaseChunkContent(Vector2Int coord) {
            if (!_spawnedContent.TryGetValue(coord, out var list)) return;
            foreach (var (instance, prefab) in list) {
                if (instance != null) _pool.Release(prefab, instance);
            }
            list.Clear();
        }

        private void TryPlaceOne(Vector2Int coord, BiomeDefinition biome, System.Random random,
            List<(GameObject instance, GameObject prefab)> list) {
            Vector3 center = _chunkManager.ChunkToWorldCenter(coord);
            float half = Mathf.Max(0f, _biomeMap.ChunkSize * 0.5f - edgeMargin);

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++) {
                var entry = PickEntry(random, biome);
                if (entry == null) return;

                float x = center.x + (float)(random.NextDouble() * 2.0 - 1.0) * half;
                float z = center.z + (float)(random.NextDouble() * 2.0 - 1.0) * half;

                if (GetSlope(x, z) > entry.maxSlopeDegrees) continue;
                if (IsNearRoad(x, z, entry.roadClearance)) continue;

                float yaw = (float)(random.NextDouble() * 360.0);
                float scale = Mathf.Lerp(entry.scaleRange.x, entry.scaleRange.y, (float)random.NextDouble());

                var position = new Vector3(x, GetGroundY(x, z), z);
                var instance = _pool.Get(entry.prefab, position, Quaternion.Euler(0f, yaw, 0f), transform);
                // Из пула приходит инстанс с масштабом прошлого использования — задаём заново.
                instance.transform.localScale = entry.prefab.transform.localScale * scale;
                list.Add((instance, entry.prefab));
                return;
            }
            // Подходящей точки не нашлось за maxPlacementAttempts попыток — объект пропускаем.
        }

        private BiomeObjectEntry PickEntry(System.Random random, BiomeDefinition biome) {
            float sanity = GetSanityFactor();

            double total = 0;
            foreach (var e in biome.Entries) total += EntryWeight(e, sanity);
            if (total <= 0) return null;

            double roll = random.NextDouble() * total;
            double cumulative = 0;
            BiomeObjectEntry last = null;
            foreach (var e in biome.Entries) {
                float w = EntryWeight(e, sanity);
                if (w <= 0f) continue;
                last = e;
                cumulative += w;
                if (roll <= cumulative) return e;
            }
            return last; // на случай ошибок округления
        }

        private static float EntryWeight(BiomeObjectEntry e, float sanityFactor) {
            if (e == null || e.prefab == null) return 0f;
            return Mathf.Max(0f, e.weight + e.lowSanityBias * sanityFactor);
        }

        private bool IsNearRoad(float x, float z, float clearance) {
            if (_biomeMap.IsRoadAt(new Vector3(x, 0f, z))) return true;
            if (clearance <= 0f) return false;
            return _biomeMap.IsRoadAt(new Vector3(x + clearance, 0f, z))
                || _biomeMap.IsRoadAt(new Vector3(x - clearance, 0f, z))
                || _biomeMap.IsRoadAt(new Vector3(x, 0f, z + clearance))
                || _biomeMap.IsRoadAt(new Vector3(x, 0f, z - clearance));
        }

        private float GetSlope(float x, float z) {
            var data = _terrain.terrainData;
            Vector3 pos = _terrain.GetPosition();
            float nx = Mathf.Clamp01((x - pos.x) / data.size.x);
            float nz = Mathf.Clamp01((z - pos.z) / data.size.z);
            return data.GetSteepness(nx, nz);
        }

        // SampleHeight возвращает высоту относительно позиции террейна — добавляем её Y.
        private float GetGroundY(float x, float z) {
            return _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.GetPosition().y;
        }

        // Заглушка до этапа 6. Сознательно не инжектю ISanityManager сейчас —
        // он ещё не зарегистрирован в LifetimeScope.
        private float GetSanityFactor() => 0f;
    }
}