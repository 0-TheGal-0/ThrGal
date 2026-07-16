using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    public class ChunkManager : MonoBehaviour, IChunkManager {
        [SerializeField] private float chunkSize = 10f;

        [Tooltip("Сколько чанков вокруг мировых координат (0,0) проверять и рисовать. " +
                 "Чисто для дебага на этапе 1 — на большом мире так делать нельзя, " +
                 "это временное решение до этапа 8 (оптимизация).")]
        [SerializeField] private int debugGridRadius = 15;

        private ILightRegistry _lightRegistry;
        private readonly Dictionary<Vector2Int, ChunkState> _chunkStates = new();

        public event Action<Vector2Int, ChunkState> OnChunkStateChanged;

        [Inject]
        public void Construct(ILightRegistry lightRegistry) {
            _lightRegistry = lightRegistry;
        }

        public Vector2Int WorldToChunkCoord(Vector3 worldPosition) {
            int x = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int z = Mathf.FloorToInt(worldPosition.z / chunkSize);
            return new Vector2Int(x, z);
        }

        public ChunkState GetChunkState(Vector2Int coord) {
            return _chunkStates.TryGetValue(coord, out var state) ? state : ChunkState.Unseen;
        }

        private void Update() {
            // Наивная проверка по всем чанкам дебаг-грида каждый кадр — специально не оптимизирую
            // сейчас (Job System/Burst это этап 8). Цель этапа 1 — проверить саму логику, не скорость.
            for (int x = -debugGridRadius; x <= debugGridRadius; x++) {
                for (int z = -debugGridRadius; z <= debugGridRadius; z++) {
                    var coord = new Vector2Int(x, z);
                    var newState = IsChunkLit(coord) ? ChunkState.Lit : ChunkState.Dark;
                    SetChunkState(coord, newState);
                }
            }
        }

        private bool IsChunkLit(Vector2Int coord) {
            Vector3 chunkCenter = new Vector3(
                (coord.x + 0.5f) * chunkSize, 0f, (coord.y + 0.5f) * chunkSize);

            foreach (var handle in _lightRegistry.ActiveSources) {
                if (handle.Source == null) continue; // источник могли уничтожить, не почистив регистрацию

                Vector3 flatSourcePos = new Vector3(handle.Source.position.x, 0f, handle.Source.position.z);
                if (Vector3.Distance(flatSourcePos, chunkCenter) <= handle.Radius)
                    return true;
            }
            return false;
        }

        private void SetChunkState(Vector2Int coord, ChunkState newState) {
            var oldState = GetChunkState(coord);
            if (oldState == newState) return;

            _chunkStates[coord] = newState;
            OnChunkStateChanged?.Invoke(coord, newState);
        }

        // Видно только в Scene View (или в Game View с включённым Gizmos) во время Play Mode.
        private void OnDrawGizmos() {
            if (!Application.isPlaying) return;

            foreach (var kvp in _chunkStates) {
                Vector3 center = new Vector3(
                    (kvp.Key.x + 0.5f) * chunkSize, 0.05f, (kvp.Key.y + 0.5f) * chunkSize);

                Gizmos.color = kvp.Value == ChunkState.Lit
                    ? new Color(1f, 0.8f, 0.2f, 0.5f)   // тёплый жёлтый — освещено
                    : new Color(0.1f, 0.1f, 0.25f, 0.5f); // холодный тёмный — тьма

                Gizmos.DrawCube(center, new Vector3(chunkSize * 0.9f, 0.05f, chunkSize * 0.9f));
            }
        }
    }
}