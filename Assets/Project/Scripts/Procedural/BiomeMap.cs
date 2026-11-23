using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Procedural {
    // Связывает запечённую маску (ChunkMapData) с определениями биомов.
    // Не использует инжектируемых зависимостей, поэтому Awake() здесь безопасен.
    public class BiomeMap : MonoBehaviour, IBiomeMap {
        [SerializeField] private ChunkMapData chunkMap;
        [SerializeField] private BiomeDefinition[] biomes;
        private readonly Dictionary<int, BiomeDefinition> _byId = new Dictionary<int, BiomeDefinition>();


        private void Awake() {
            _byId.Clear();
            MaxFootprintRadius = 0f;
            if (biomes != null) {
                foreach (var b in biomes) {
                    if (b == null) continue;
                    if (_byId.ContainsKey(b.BiomeId))
                        Debug.LogWarning($"BiomeMap: повторяющийся biomeId {b.BiomeId} ({b.name}), пропущен.");
                    else
                        _byId.Add(b.BiomeId, b);
                    foreach (var e in b.Entries) {
                        if (e == null || e.prefab == null) continue;
                        MaxFootprintRadius = Mathf.Max(MaxFootprintRadius, e.footprintRadius * e.scaleRange.y);
                    }
                }
            }
            if (chunkMap == null || !chunkMap.HasBiomes)
                Debug.LogWarning("BiomeMap: ChunkMapData не назначен или маска биомов не запечена (Bake Biome Mask).");
        }

        // --- IBiomeMap ---
        public bool TryGetBiomeId(Vector2Int coord, out int biomeId) {
            if (chunkMap != null && chunkMap.TryGetBiomeId(coord, out biomeId) && biomeId != 0)
                return true;
            biomeId = 0;
            return false;
        }

        public float GetRoadCoverage(Vector2Int coord) {
            return chunkMap != null && chunkMap.TryGetRoadCoverage(coord, out float c) ? c : 0f;
        }

        // --- Для Procedural (спавнер на срезе 6) ---
        public bool TryGetDefinition(Vector2Int coord, out BiomeDefinition definition) {
            if (TryGetBiomeId(coord, out int id) && _byId.TryGetValue(id, out definition))
                return true;
            definition = null;
            return false;
        }
        // Биом под точкой по маске: разрешение маски, а не чанка.
        public bool TryGetDefinitionAt(Vector3 worldPosition, out BiomeDefinition definition) {
            if (chunkMap != null && chunkMap.TryGetBiomeIdAt(worldPosition, out int id)
                && _byId.TryGetValue(id, out definition))
                return true;
            definition = null;
            return false;
        }
        public float ChunkSize => chunkMap != null ? chunkMap.ChunkSize : 10f;
        // Самый большой отпечаток среди объектов всех биомов (с максимальным масштабом).
        public float MaxFootprintRadius { get; private set; }

        public bool IsRoadAt(Vector3 worldPosition) {
            return chunkMap != null && chunkMap.IsRoadAt(worldPosition);
        }


    }
}