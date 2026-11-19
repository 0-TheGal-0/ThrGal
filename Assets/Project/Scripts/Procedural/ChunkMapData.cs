using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.Procedural {
    // Запечённые данные рельефа по сетке чанков. Рельеф статичен, поэтому считаем один раз
    // в редакторе и сохраняем в ассет, а в рантайме только читаем.
    [CreateAssetMenu(fileName = "ChunkMapData", menuName = "Game/Chunk Map Data")]
    public class ChunkMapData : ScriptableObject {
        [Header("Настройки запекания")]
        [Tooltip("Должен совпадать с chunkSize у ChunkManager. Если меняете — перезапеките.")]
        [SerializeField] private float chunkSize = 10f;

        [Tooltip("Сколько точек по каждой оси внутри чанка проверять на уклон (5 = сетка 5x5).")]
        [SerializeField, Min(2)] private int slopeSamplesPerAxis = 5;

        [Header("Результат (заполняется запекателем, руками не править)")]
        [SerializeField] private Vector2Int originChunk;
        [SerializeField] private int widthChunks;
        [SerializeField] private int heightChunks;
        [SerializeField] private float[] minY;
        [SerializeField] private float[] maxY;
        [SerializeField] private float[] maxSlope;
        [SerializeField] private float[] centerY;

        public float ChunkSize => chunkSize;
        public Vector2Int OriginChunk => originChunk;
        public int WidthChunks => widthChunks;
        public int HeightChunks => heightChunks;

        public bool IsBaked =>
            widthChunks > 0 && heightChunks > 0
            && minY != null && minY.Length == widthChunks * heightChunks
            && centerY != null && centerY.Length == widthChunks * heightChunks;

        public bool TryGetIndex(Vector2Int coord, out int index) {
            int x = coord.x - originChunk.x;
            int z = coord.y - originChunk.y;
            if (x < 0 || z < 0 || x >= widthChunks || z >= heightChunks) {
                index = -1;
                return false;
            }
            index = z * widthChunks + x;
            return true;
        }

        public bool TryGetHeightRange(Vector2Int coord, out float min, out float max) {
            if (IsBaked && TryGetIndex(coord, out int i)) {
                min = minY[i];
                max = maxY[i];
                return true;
            }
            min = max = 0f;
            return false;
        }

        public bool TryGetMaxSlope(Vector2Int coord, out float slopeDegrees) {
            if (IsBaked && TryGetIndex(coord, out int i)) {
                slopeDegrees = maxSlope[i];
                return true;
            }
            slopeDegrees = 0f;
            return false;
        }

        public bool TryGetCenterY(Vector2Int coord, out float y) {
            if (IsBaked && TryGetIndex(coord, out int i)) {
                y = centerY[i];
                return true;
            }
            y = 0f;
            return false;
        }

#if UNITY_EDITOR
        [ContextMenu("Bake From Active Terrain")]
        private void BakeFromActiveTerrain() {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) {
                Debug.LogError("ChunkMapData: на открытой сцене нет активного Terrain.");
                return;
            }

            var data = terrain.terrainData;
            Vector3 terrainPos = terrain.GetPosition();
            Vector3 size = data.size;

            originChunk = new Vector2Int(
                Mathf.FloorToInt(terrainPos.x / chunkSize),
                Mathf.FloorToInt(terrainPos.z / chunkSize));
            int endX = Mathf.CeilToInt((terrainPos.x + size.x) / chunkSize);
            int endZ = Mathf.CeilToInt((terrainPos.z + size.z) / chunkSize);
            widthChunks = endX - originChunk.x;
            heightChunks = endZ - originChunk.y;

            int count = widthChunks * heightChunks;
            minY = new float[count];
            maxY = new float[count];
            maxSlope = new float[count];
            centerY = new float[count];

            // Карта высот целиком: heights[z, x], значения 0..1 от высоты size.y.
            int res = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, res, res);
            float stepX = size.x / (res - 1);
            float stepZ = size.z / (res - 1);

            float globalMin = float.MaxValue, globalMax = float.MinValue, globalSlope = 0f;
            int centerOutOfRange = 0;

            for (int cz = 0; cz < heightChunks; cz++) {
                for (int cx = 0; cx < widthChunks; cx++) {
                    int index = cz * widthChunks + cx;

                    // Границы чанка в локальных координатах терейна.
                    float localX0 = (originChunk.x + cx) * chunkSize - terrainPos.x;
                    float localZ0 = (originChunk.y + cz) * chunkSize - terrainPos.z;
                    float localX1 = localX0 + chunkSize;
                    float localZ1 = localZ0 + chunkSize;

                    // Чанк целиком за пределами терейна.
                    if (localX1 <= 0f || localX0 >= size.x || localZ1 <= 0f || localZ0 >= size.z) {
                        minY[index] = maxY[index] = terrainPos.y;
                        centerY[index] = terrainPos.y;
                        maxSlope[index] = 0f;
                        continue;
                    }

                    // min/max по всем узлам карты высот, которые покрывают чанк (с запасом
                    // в один узел наружу). Между узлами высота интерполируется, поэтому
                    // за пределы этих значений она выйти не может — оценка консервативная.
                    int ix0 = Mathf.Clamp(Mathf.FloorToInt(localX0 / stepX), 0, res - 1);
                    int ix1 = Mathf.Clamp(Mathf.CeilToInt(localX1 / stepX), 0, res - 1);
                    int iz0 = Mathf.Clamp(Mathf.FloorToInt(localZ0 / stepZ), 0, res - 1);
                    int iz1 = Mathf.Clamp(Mathf.CeilToInt(localZ1 / stepZ), 0, res - 1);

                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int iz = iz0; iz <= iz1; iz++) {
                        for (int ix = ix0; ix <= ix1; ix++) {
                            float h = heights[iz, ix];
                            if (h < lo) lo = h;
                            if (h > hi) hi = h;
                        }
                    }
                    minY[index] = terrainPos.y + lo * size.y;
                    maxY[index] = terrainPos.y + hi * size.y;
                    float nxc = Mathf.Clamp01((localX0 + chunkSize * 0.5f) / size.x);
                    float nzc = Mathf.Clamp01((localZ0 + chunkSize * 0.5f) / size.z);
                    centerY[index] = terrainPos.y + data.GetInterpolatedHeight(nxc, nzc);
                    if (centerY[index] < minY[index] - 0.01f || centerY[index] > maxY[index] + 0.01f)
                        centerOutOfRange++;

                    // Максимальный уклон (в градусах) по сетке точек внутри чанка.
                    int n = slopeSamplesPerAxis;
                    float slope = 0f;
                    for (int sz = 0; sz < n; sz++) {
                        for (int sx = 0; sx < n; sx++) {
                            float nx = Mathf.Clamp01((localX0 + sx / (float)(n - 1) * chunkSize) / size.x);
                            float nz = Mathf.Clamp01((localZ0 + sz / (float)(n - 1) * chunkSize) / size.z);
                            slope = Mathf.Max(slope, data.GetSteepness(nx, nz));
                        }
                    }
                    maxSlope[index] = slope;

                    globalMin = Mathf.Min(globalMin, minY[index]);
                    globalMax = Mathf.Max(globalMax, maxY[index]);
                    globalSlope = Mathf.Max(globalSlope, slope);
                }
            }

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();

            Debug.Log($"ChunkMapData: запечено {widthChunks}x{heightChunks} чанков, origin = {originChunk}. " +
                      $"Высота: от {globalMin:F1} до {globalMax:F1} м. Максимальный уклон: {globalSlope:F1}°."+$"Центр вне [minY, maxY]: {centerOutOfRange} (должно быть 0)");
        }
#endif
    }
}