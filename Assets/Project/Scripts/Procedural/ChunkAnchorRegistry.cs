using System.Collections.Generic;
using UnityEngine;
using Game.Core;

namespace Game.Procedural {
    public class ChunkAnchorRegistry : MonoBehaviour, IChunkAnchorRegistry {
        private readonly HashSet<Vector2Int> _anchors = new();

        public bool IsAnchor(Vector2Int coord) => _anchors.Contains(coord);
        public void RegisterAnchor(Vector2Int coord) => _anchors.Add(coord);
        public void UnregisterAnchor(Vector2Int coord) => _anchors.Remove(coord);
    }
}