using System.Collections.Generic;
using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    // Помечает ВСЕ чанки, попадающие в прямоугольную зону вокруг объекта, как "якорные" —
    // ChunkContentSpawner их не тронет при регенерации. Сама зона задаётся в мировых
    // единицах (areaSize), а не в чанках — так дизайнер может подогнать её под реальный
    // футпринт структуры на сцене, не думая про chunkSize.
    public class NarrativeAnchor : MonoBehaviour {
        [Tooltip("Размер сюжетной зоны в мировых единицах (X, Z), центр — позиция этого объекта. " +
                 "Все чанки, хотя бы частично попадающие в этот прямоугольник, будут защищены " +
                 "от регенерации контента. Для структуры мельче одного чанка можно оставить " +
                 "значение по умолчанию (10x10) — покроет ровно один чанк при chunkSize = 10.")]
        [SerializeField] private Vector2 areaSize = new Vector2(10f, 10f);

        private IChunkAnchorRegistry _anchorRegistry;
        private IChunkManager _chunkManager;

        // Храним список, что именно зарегистрировали, чтобы на OnDestroy снять защиту
        // ровно с тех же координат, а не пересчитывать заново (areaSize могли поменять
        // в рантайме через SetRadius-подобный метод — тут такого нет, но так безопаснее).
        private readonly List<Vector2Int> _coveredCoords = new();

        [Inject]
        public void Construct(IChunkAnchorRegistry anchorRegistry, IChunkManager chunkManager) {
            _anchorRegistry = anchorRegistry;
            _chunkManager = chunkManager;
        }

        private void Start() {
            RegisterCoveredChunks();
        }

        private void OnDestroy() {
            foreach (var coord in _coveredCoords) {
                _anchorRegistry?.UnregisterAnchor(coord);
            }
        }

        private void RegisterCoveredChunks() {
            Vector3 center = transform.position;
            Vector3 halfExtents = new Vector3(areaSize.x, 0f, areaSize.y) * 0.5f;

            Vector2Int minCoord = _chunkManager.WorldToChunkCoord(center - halfExtents);
            Vector2Int maxCoord = _chunkManager.WorldToChunkCoord(center + halfExtents);

            for (int x = minCoord.x; x <= maxCoord.x; x++) {
                for (int z = minCoord.y; z <= maxCoord.y; z++) {
                    var coord = new Vector2Int(x, z);
                    _anchorRegistry.RegisterAnchor(coord);
                    _coveredCoords.Add(coord);
                }
            }
        }

        // Видно в Scene View при выделении объекта — прямоугольник должен визуально
        // совпадать с реальным футпринтом структуры на сцене.
        private void OnDrawGizmosSelected() {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
        }
    }
}