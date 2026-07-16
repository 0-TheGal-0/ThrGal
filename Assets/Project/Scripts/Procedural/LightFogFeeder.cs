using UnityEngine;
using VContainer;
using Game.Core;

namespace Game.Procedural {
    // Раз в кадр складывает позиции/радиусы источников света в глобальные shader-переменные.
    // Fullscreen-шейдер тумана читает их напрямую по имени, без прямой ссылки на C#-код —
    // это стандартный способ передать данные из геймплея в кастомный рендер-пасс URP.
    public class LightFogFeeder : MonoBehaviour {
        private const int MaxLights = 8; // ограничение — сколько источников шейдер учитывает одновременно

        private static readonly int LightPositionsId = Shader.PropertyToID("_FogLightPositions");
        private static readonly int LightRadiiId = Shader.PropertyToID("_FogLightRadii");
        private static readonly int LightCountId = Shader.PropertyToID("_FogLightCount");

        private ILightRegistry _lightRegistry;
        private Vector4[] _positionsBuffer;
        private float[] _radiiBuffer;

        [Inject]
        public void Construct(ILightRegistry lightRegistry) {
            _lightRegistry = lightRegistry;
        }

        private void Awake() {
            _positionsBuffer = new Vector4[MaxLights];
            _radiiBuffer = new float[MaxLights];
        }

        private void Update() {
            var sources = _lightRegistry.ActiveSources;
            int count = Mathf.Min(sources.Count, MaxLights);

            for (int i = 0; i < count; i++) {
                var handle = sources[i];
                _positionsBuffer[i] = handle.Source != null ? handle.Source.position : Vector4.zero;
                _radiiBuffer[i] = handle.Radius;
            }

            Shader.SetGlobalVectorArray(LightPositionsId, _positionsBuffer);
            Shader.SetGlobalFloatArray(LightRadiiId, _radiiBuffer);
            Shader.SetGlobalInt(LightCountId, count);
        }
    }
}