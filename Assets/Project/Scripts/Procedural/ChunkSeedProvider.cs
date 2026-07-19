using UnityEngine;

namespace Game.Procedural {
    // Один System.Random на (globalSeed, chunkCoord) — детерминированно, воспроизводимо,
    // не зависит от порядка вызовов и от того, сколько раз чанк уже регенерировался.
    public class ChunkSeedProvider {
        private readonly int _globalSeed;

        // VContainer подставит globalSeed сам — см. регистрацию с WithParameter в LifetimeScope.
        public ChunkSeedProvider(int globalSeed) {
            _globalSeed = globalSeed;
        }

        public System.Random GetRandom(Vector2Int coord) {
            // Намеренно не Vector2Int.GetHashCode() — он не документирован как стабильный
            // между версиями/платформами Unity, а нам нужна гарантированная воспроизводимость.
            unchecked {
                int hash = 17;
                hash = hash * 31 + _globalSeed;
                hash = hash * 31 + coord.x;
                hash = hash * 31 + coord.y;
                return new System.Random(hash);
            }
        }
    }
}