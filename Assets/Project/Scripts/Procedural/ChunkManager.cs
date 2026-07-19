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

        [Tooltip("Сколько секунд чанк проведёт в состоянии Fading, прежде чем перейти в Dark " +
                 "(и стать кандидатом на регенерацию). Даёт игроку 'окно', чтобы вернуться " +
                 "со светом и не потерять чанк.")]
        [SerializeField] private float fadeDuration = 3f;

        private ILightRegistry _lightRegistry;
        private Camera _mainCamera;

        private readonly Dictionary<Vector2Int, ChunkState> _chunkStates = new();
        private readonly Dictionary<Vector2Int, float> _fadeTimers = new();

        public event Action<Vector2Int, ChunkState> OnChunkStateChanged;

        [Inject]
        public void Construct(ILightRegistry lightRegistry) {
            _lightRegistry = lightRegistry;
        }

        private void Start() {
            _mainCamera = Camera.main;
        }

        public Vector2Int WorldToChunkCoord(Vector3 worldPosition) {
            int x = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int z = Mathf.FloorToInt(worldPosition.z / chunkSize);
            return new Vector2Int(x, z);
        }

        public Vector3 ChunkToWorldCenter(Vector2Int coord) {
            return new Vector3((coord.x + 0.5f) * chunkSize, 0f, (coord.y + 0.5f) * chunkSize);
        }

        public ChunkState GetChunkState(Vector2Int coord) {
            return _chunkStates.TryGetValue(coord, out var state) ? state : ChunkState.Unseen;
        }

        private void Update() {
            for (int x = -debugGridRadius; x <= debugGridRadius; x++) {
                for (int z = -debugGridRadius; z <= debugGridRadius; z++) {
                    var coord = new Vector2Int(x, z);
                    UpdateChunk(coord);
                }
            }
        }

        private void UpdateChunk(Vector2Int coord) {
            bool lit = IsChunkLit(coord);
            var currentState = GetChunkState(coord);

            if (lit) {
                _fadeTimers.Remove(coord);
                SetChunkState(coord, ChunkState.Lit);
                return;
            }

            switch (currentState) {
                case ChunkState.Lit:
                    _fadeTimers[coord] = 0f;
                    SetChunkState(coord, ChunkState.Fading);
                    break;

                case ChunkState.Fading:
                    float timer = _fadeTimers.TryGetValue(coord, out var t) ? t : 0f;
                    timer += Time.deltaTime;
                    _fadeTimers[coord] = timer;

                    if (timer >= fadeDuration && !IsChunkVisibleToCamera(coord)) {
                        _fadeTimers.Remove(coord);
                        SetChunkState(coord, ChunkState.Dark);
                    }
                    break;

                case ChunkState.Unseen:
                case ChunkState.Dark:
                    break;
            }
        }

        private bool IsChunkLit(Vector2Int coord) {
            Vector3 chunkCenter = ChunkToWorldCenter(coord);

            foreach (var handle in _lightRegistry.ActiveSources) {
                if (handle.Source == null) continue;

                Vector3 flatSourcePos = new Vector3(handle.Source.position.x, 0f, handle.Source.position.z);
                if (Vector3.Distance(flatSourcePos, chunkCenter) <= handle.Radius)
                    return true;
            }
            return false;
        }

        private bool IsChunkVisibleToCamera(Vector2Int coord) {
            if (_mainCamera == null) return false;

            Vector3 center = ChunkToWorldCenter(coord);
            var bounds = new Bounds(center, new Vector3(chunkSize, chunkSize, chunkSize));

            var planes = GeometryUtility.CalculateFrustumPlanes(_mainCamera);
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        private void SetChunkState(Vector2Int coord, ChunkState newState) {
            var oldState = GetChunkState(coord);
            if (oldState == newState) return;

            _chunkStates[coord] = newState;
            OnChunkStateChanged?.Invoke(coord, newState);
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying) return;

            foreach (var kvp in _chunkStates) {
                Vector3 center = new Vector3(
                    (kvp.Key.x + 0.5f) * chunkSize, 0.05f, (kvp.Key.y + 0.5f) * chunkSize);

                Gizmos.color = kvp.Value switch {
                    ChunkState.Lit => new Color(1f, 0.8f, 0.2f, 0.5f),
                    ChunkState.Fading => new Color(0.9f, 0.4f, 0.1f, 0.5f),
                    ChunkState.Dark => new Color(0.1f, 0.1f, 0.25f, 0.5f),
                    _ => new Color(0.3f, 0.3f, 0.3f, 0.2f)
                };

                Gizmos.DrawCube(center, new Vector3(chunkSize * 0.9f, 0.05f, chunkSize * 0.9f));
            }
        }
    }
}