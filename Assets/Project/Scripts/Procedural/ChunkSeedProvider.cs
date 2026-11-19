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

        // Финализатор SplitMix64: превращает "почти одинаковые" входы в далёкие друг от друга числа.
        private static ulong Mix(ulong z) {
            unchecked {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        // visitCount — сколько раз этот чанк уже регенерировался. Один и тот же
        // (globalSeed, coord, visitCount) всегда даёт один и тот же результат,
        // но следующий visitCount даёт другой.
        public System.Random GetRandom(Vector2Int coord, int visitCount) {
            unchecked {
                ulong h = Mix((uint)_globalSeed);
                h = Mix(h ^ (uint)coord.x);
                h = Mix(h ^ (uint)coord.y);
                h = Mix(h ^ (uint)visitCount);
                return new System.Random((int)(h ^ (h >> 32)));
            }
        }
    }
}