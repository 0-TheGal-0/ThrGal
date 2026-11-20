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
        [Header("Рельеф")]
        [Tooltip("Запечённые данные рельефа. Если не назначено или не запечено — старое поведение (всё на y = 0).")]
        [SerializeField] private ChunkMapData chunkMap;

        [Tooltip("Запас по высоте над рельефом в проверке видимости — чтобы учесть деревья и дома на чанке.")]
        [SerializeField] private float contentHeightMargin = 10f;
        [Tooltip("Обрабатывать все чанки мира по границам ChunkMapData (а не квадрат debugGridRadius). " +
         "Если карта не запечена, сработает debugGridRadius.")]
        [SerializeField] private bool useWorldBounds = true;

        private ILightRegistry _lightRegistry;
        private Camera _mainCamera;
        private int _minX, _maxX, _minZ, _maxZ;

        private readonly Dictionary<Vector2Int, ChunkState> _chunkStates = new();
        // Чанки, которые сейчас Lit или Fading: только они могут поменяться без участия света.
        private readonly HashSet<Vector2Int> _activeChunks = new();
        // Буфер кандидатов на этот кадр. Переиспользуем, чтобы не создавать мусор каждый кадр.
        private readonly HashSet<Vector2Int> _candidates = new();
        private readonly Dictionary<Vector2Int, float> _fadeTimers = new();

        public event Action<Vector2Int, ChunkState> OnChunkStateChanged;

        [Inject]
        public void Construct(ILightRegistry lightRegistry) {
            _lightRegistry = lightRegistry;
        }

        private void Start() {
            _mainCamera = Camera.main;

            if (chunkMap == null || !chunkMap.IsBaked) {
                Debug.LogWarning("ChunkManager: ChunkMapData не назначен или не запечён — работаю без рельефа (y = 0).");
            } else if (!Mathf.Approximately(chunkMap.ChunkSize, chunkSize)) {
                Debug.LogError($"ChunkManager: chunkSize ({chunkSize}) не совпадает с ChunkMapData ({chunkMap.ChunkSize}). Перезапеките.");
            }
            if (useWorldBounds && chunkMap != null && chunkMap.IsBaked) {
                _minX = chunkMap.OriginChunk.x;
                _minZ = chunkMap.OriginChunk.y;
                _maxX = _minX + chunkMap.WidthChunks - 1;
                _maxZ = _minZ + chunkMap.HeightChunks - 1;
            } else {
                _minX = _minZ = -debugGridRadius;
                _maxX = _maxZ = debugGridRadius;
            }
        }

        public Vector2Int WorldToChunkCoord(Vector3 worldPosition) {
            int x = Mathf.FloorToInt(worldPosition.x / chunkSize);
            int z = Mathf.FloorToInt(worldPosition.z / chunkSize);
            return new Vector2Int(x, z);
        }

        public Vector3 ChunkToWorldCenter(Vector2Int coord) {
            float y = (chunkMap != null && chunkMap.TryGetCenterY(coord, out float groundY)) ? groundY : 0f;
            return new Vector3((coord.x + 0.5f) * chunkSize, y, (coord.y + 0.5f) * chunkSize);
        }

        public ChunkState GetChunkState(Vector2Int coord) {
            return _chunkStates.TryGetValue(coord, out var state) ? state : ChunkState.Unseen;
        }

        private void Update() {
            _candidates.Clear();

            // 1. Всё, что уже Lit/Fading: нужно проверять, не погасло ли и не истёк ли таймер.
            foreach (var coord in _activeChunks) _candidates.Add(coord);

            // 2. Чанки в квадрате вокруг каждого источника: они могут стать Lit.
            foreach (var handle in _lightRegistry.ActiveSources) {
                if (handle.Source == null) continue;

                Vector3 p = handle.Source.position;
                int fromX = Mathf.Max(_minX, Mathf.FloorToInt((p.x - handle.Radius) / chunkSize));
                int toX = Mathf.Min(_maxX, Mathf.FloorToInt((p.x + handle.Radius) / chunkSize));
                int fromZ = Mathf.Max(_minZ, Mathf.FloorToInt((p.z - handle.Radius) / chunkSize));
                int toZ = Mathf.Min(_maxZ, Mathf.FloorToInt((p.z + handle.Radius) / chunkSize));

                for (int x = fromX; x <= toX; x++)
                    for (int z = fromZ; z <= toZ; z++)
                        _candidates.Add(new Vector2Int(x, z));
            }

            foreach (var coord in _candidates) UpdateChunk(coord);
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
            bool use3D = chunkMap != null && chunkMap.IsBaked;

            foreach (var handle in _lightRegistry.ActiveSources) {
                if (handle.Source == null) continue;

                Vector3 sourcePos = handle.Source.position;
                if (!use3D) sourcePos.y = 0f; // без рельефа — как раньше, в плоскости XZ

                if (Vector3.Distance(sourcePos, chunkCenter) <= handle.Radius)
                    return true;
            }
            return false;
        }

        private bool IsChunkVisibleToCamera(Vector2Int coord) {
            if (_mainCamera == null) return false;

            Vector3 center = ChunkToWorldCenter(coord);
            Bounds bounds;
            if (chunkMap != null && chunkMap.TryGetHeightRange(coord, out float minY, out float maxY)) {
                float top = maxY + contentHeightMargin;
                bounds = new Bounds(
                    new Vector3(center.x, (minY + top) * 0.5f, center.z),
                    new Vector3(chunkSize, top - minY, chunkSize));
            } else {
                bounds = new Bounds(center, new Vector3(chunkSize, chunkSize, chunkSize));
            }

            var planes = GeometryUtility.CalculateFrustumPlanes(_mainCamera);
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        private void SetChunkState(Vector2Int coord, ChunkState newState) {
            var oldState = GetChunkState(coord);
            if (oldState == newState) return;

            _chunkStates[coord] = newState;

            if (newState == ChunkState.Lit || newState == ChunkState.Fading)
                _activeChunks.Add(coord);
            else
                _activeChunks.Remove(coord);

            OnChunkStateChanged?.Invoke(coord, newState);
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying) return;

            foreach (var kvp in _chunkStates) {
                float y = chunkMap != null && chunkMap.TryGetHeightRange(kvp.Key, out _, out float topY) ? topY + 0.05f : 0.05f;
                Vector3 center = new Vector3((kvp.Key.x + 0.5f) * chunkSize, y, (kvp.Key.y + 0.5f) * chunkSize);

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