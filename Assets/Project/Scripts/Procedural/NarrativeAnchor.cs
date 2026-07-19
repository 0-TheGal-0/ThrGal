using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    // Помечает чанк, в котором стоит этот объект, как "якорный" — ChunkContentSpawner
    // не будет его трогать при регенерации. Сам объект ничего не спавнит и не хранит контент,
    // только регистрирует координату.
    public class NarrativeAnchor : MonoBehaviour {
        private IChunkAnchorRegistry _anchorRegistry;
        private IChunkManager _chunkManager;
        private Vector2Int _coord;

        [Inject]
        public void Construct(IChunkAnchorRegistry anchorRegistry, IChunkManager chunkManager) {
            _anchorRegistry = anchorRegistry;
            _chunkManager = chunkManager;
        }

        private void Start() {
            _coord = _chunkManager.WorldToChunkCoord(transform.position);
            _anchorRegistry.RegisterAnchor(_coord);
        }

        private void OnDestroy() {
            _anchorRegistry?.UnregisterAnchor(_coord);
        }
    }
}