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
        [SerializeField] private byte[] biomeId;
        [SerializeField] private float[] roadCoverage; // доля площади чанка под дорогой, 0..1
        [SerializeField] private Vector2 maskOriginXZ; // мировые X,Z левого нижнего угла маски
        [SerializeField] private Vector2 maskSizeXZ;   // размер маски в метрах
        [Tooltip("Маска: R = id биома (0 = нет), G = дорога (>=128). Верх картинки = +Z.")]
        [SerializeField] private Texture2D biomeMask;

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
        public bool HasBiomes =>
    IsBaked && biomeId != null && biomeId.Length == widthChunks * heightChunks
    && roadCoverage != null && roadCoverage.Length == widthChunks * heightChunks;

        public bool TryGetBiomeId(Vector2Int coord, out int id) {
            if (HasBiomes && TryGetIndex(coord, out int i)) {
                id = biomeId[i];
                return true;
            }
            id = 0;
            return false;
        }

        public bool TryGetRoadCoverage(Vector2Int coord, out float coverage) {
            if (HasBiomes && TryGetIndex(coord, out int i)) {
                coverage = roadCoverage[i];
                return true;
            }
            coverage = 0f;
            return false;
        }
        // Пиксель маски под мировой точкой (X, Z). false, если маски нет или точка вне её.
        private bool TryGetMaskPixel(Vector3 world, out int px, out int py) {
            px = py = 0;
            if (!HasBiomes || biomeMask == null || maskSizeXZ.x <= 0f || maskSizeXZ.y <= 0f) return false;

            float u = (world.x - maskOriginXZ.x) / maskSizeXZ.x;
            float v = (world.z - maskOriginXZ.y) / maskSizeXZ.y;
            if (u < 0f || u >= 1f || v < 0f || v >= 1f) return false;

            px = Mathf.Clamp((int)(u * biomeMask.width), 0, biomeMask.width - 1);
            py = Mathf.Clamp((int)(v * biomeMask.height), 0, biomeMask.height - 1);
            return true;
        }

        // Точечная проверка дороги по маске (G >= 0.5). Поведение то же, что было.
        public bool IsRoadAt(Vector3 world) {
            return TryGetMaskPixel(world, out int px, out int py)
                && biomeMask.GetPixel(px, py).g >= 0.5f;
        }

        // Id биома под точкой по R-каналу маски. 0 («нет биома») или точка вне маски -> false.
        public bool TryGetBiomeIdAt(Vector3 world, out int id) {
            id = 0;
            if (!TryGetMaskPixel(world, out int px, out int py)) return false;
            id = Mathf.RoundToInt(biomeMask.GetPixel(px, py).r * 255f);
            return id != 0;
        }

#if UNITY_EDITOR
        [ContextMenu("Bake From Active Terrain")]
        [ContextMenu("Bake Biome Mask")]
        private void BakeBiomeMask() {
            if (!IsBaked) {
                Debug.LogError("ChunkMapData: сначала выполните Bake From Active Terrain.");
                return;
            }
            if (biomeMask == null) {
                Debug.LogError("ChunkMapData: не назначена biomeMask.");
                return;
            }
            if (!biomeMask.isReadable) {
                Debug.LogError("ChunkMapData: у текстуры маски включите Read/Write.");
                return;
            }
            var terrain = Terrain.activeTerrain;
            if (terrain == null) {
                Debug.LogError("ChunkMapData: на открытой сцене нет активного Terrain.");
                return;
            }

            Vector3 terrainPos = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            int texW = biomeMask.width;
            int texH = biomeMask.height;
            Color32[] pixels = biomeMask.GetPixels32(); // индекс = y * texW + x, строка 0 = низ (-Z)

            int count = widthChunks * heightChunks;
            biomeId = new byte[count];
            roadCoverage = new float[count];

            var biomeCounts = new int[256];
            var totalPerBiome = new int[256];
            int chunksWithRoad = 0;

            for (int cz = 0; cz < heightChunks; cz++) {
                for (int cx = 0; cx < widthChunks; cx++) {
                    int index = cz * widthChunks + cx;

                    // Границы чанка в локальных координатах террейна -> пиксели маски.
                    float lx0 = (originChunk.x + cx) * chunkSize - terrainPos.x;
                    float lz0 = (originChunk.y + cz) * chunkSize - terrainPos.z;
                    int px0 = Mathf.Clamp(Mathf.FloorToInt(lx0 / size.x * texW), 0, texW);
                    int px1 = Mathf.Clamp(Mathf.FloorToInt((lx0 + chunkSize) / size.x * texW), 0, texW);
                    int py0 = Mathf.Clamp(Mathf.FloorToInt(lz0 / size.z * texH), 0, texH);
                    int py1 = Mathf.Clamp(Mathf.FloorToInt((lz0 + chunkSize) / size.z * texH), 0, texH);

                    System.Array.Clear(biomeCounts, 0, biomeCounts.Length);
                    int total = 0, road = 0;
                    for (int py = py0; py < py1; py++) {
                        for (int px = px0; px < px1; px++) {
                            Color32 c = pixels[py * texW + px];
                            biomeCounts[c.r]++;
                            if (c.g >= 128) road++;
                            total++;
                        }
                    }
                    if (total == 0) continue; // чанк вне маски: биом 0, дороги нет

                    // Биом чанка = самый частый id среди его пикселей (при равенстве - меньший id).
                    int best = 0;
                    for (int id = 1; id < 256; id++)
                        if (biomeCounts[id] > biomeCounts[best]) best = id;
                    biomeId[index] = (byte)best;
                    totalPerBiome[best]++;

                    roadCoverage[index] = road / (float)total;
                    if (roadCoverage[index] > 0f) chunksWithRoad++;
                }
            }
            maskOriginXZ = new Vector2(terrainPos.x, terrainPos.z);
            maskSizeXZ = new Vector2(size.x, size.z);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();

            string stats = "";
            for (int id = 0; id < 256; id++)
                if (totalPerBiome[id] > 0) stats += $" биом {id}: {totalPerBiome[id]};";
            Debug.Log($"ChunkMapData: маска запечена, чанков с дорогой: {chunksWithRoad}.{stats}");
        }

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