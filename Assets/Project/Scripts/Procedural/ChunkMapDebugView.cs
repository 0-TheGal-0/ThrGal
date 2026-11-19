using UnityEngine;

namespace Game.Procedural {
    // Чисто отладочный компонент: рисует в Scene View запечённые данные чанков.
    // В DI не регистрируется и ничего не инжектит.
    public class ChunkMapDebugView : MonoBehaviour {
        [SerializeField] private ChunkMapData chunkMap;
        [SerializeField] private bool draw = true;

        [Tooltip("Вокруг кого рисовать. Если не задано — вокруг этого объекта.")]
        [SerializeField] private Transform center;

        [Tooltip("Радиус отрисовки в чанках (20 = 41x41 чанков). Рисовать все 10 000 сразу тяжело.")]
        [SerializeField, Min(1)] private int radiusInChunks = 20;

        [Tooltip("При каком уклоне (градусы) чанк становится полностью красным.")]
        [SerializeField] private float slopeForRed = 40f;

        private void OnDrawGizmos() {
            if (!draw || chunkMap == null || !chunkMap.IsBaked) return;

            Vector3 centerPos = center != null ? center.position : transform.position;
            float size = chunkMap.ChunkSize;
            var centerCoord = new Vector2Int(
                Mathf.FloorToInt(centerPos.x / size),
                Mathf.FloorToInt(centerPos.z / size));

            for (int x = -radiusInChunks; x <= radiusInChunks; x++) {
                for (int z = -radiusInChunks; z <= radiusInChunks; z++) {
                    var coord = new Vector2Int(centerCoord.x + x, centerCoord.y + z);
                    if (!chunkMap.TryGetHeightRange(coord, out float minY, out float maxY)) continue;
                    chunkMap.TryGetMaxSlope(coord, out float slope);

                    // Зелёный (ровно) → жёлтый → красный (>= slopeForRed).
                    float t = Mathf.Clamp01(slope / slopeForRed);
                    Color color = t < 0.5f
                        ? Color.Lerp(Color.green, Color.yellow, t * 2f)
                        : Color.Lerp(Color.yellow, Color.red, (t - 0.5f) * 2f);

                    // Бокс от minY до maxY — ровно та рамка, которую позже получит проверка видимости.
                    float height = Mathf.Max(maxY - minY, 0.2f);
                    var boxCenter = new Vector3(
                        (coord.x + 0.5f) * size, (minY + maxY) * 0.5f, (coord.y + 0.5f) * size);
                    var boxSize = new Vector3(size * 0.95f, height, size * 0.95f);

                    color.a = 0.35f;
                    Gizmos.color = color;
                    Gizmos.DrawCube(boxCenter, boxSize);
                }
            }
        }
    }
}