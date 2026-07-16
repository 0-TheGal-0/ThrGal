Shader "Hidden/FogCarve"
{
    Properties
    {
        // Подберите так же, как в Window > Rendering > Lighting > Environment > Fog (Слой 1) —
        // это только визуальное соответствие, сами настройки RenderSettings.fog никак
        // не связаны с этим шейдером автоматически.
        _FogColor("Fog Color", Color) = (0.04, 0.047, 0.078, 1)
        _FogDensity("Fog Density", Range(0.0, 0.5)) = 0.1

        // Насколько плавно туман нарастает на границе радиуса источника света, в метрах.
        // 0 = резкая граница ("стена тумана" ровно по кругу), больше = мягкий градиент.
        _FogClearRadiusPad("Light Edge Softness (m)", Range(0.1, 10)) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "FogCarvePass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define MAX_FOG_LIGHTS 8

            // Глобальные переменные — их публикует LightFogFeeder.cs каждый кадр через
            // Shader.SetGlobalVectorArray/SetGlobalInt. Имена должны совпадать 1-в-1.
            float4 _FogLightPositions[MAX_FOG_LIGHTS];
            float _FogLightRadii[MAX_FOG_LIGHTS];
            int _FogLightCount;

            // Обычные свойства материала — крутятся в инспекторе материала.
            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float _FogDensity;
                float _FogClearRadiusPad;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);

                // Если геометрии нет (смотрим в небо/скайбокс) — не трогаем, там уже
                // работает базовый Слой 1 через RenderSettings.fog на самом скайбоксе.
                if (rawDepth <= 0.0001)
                {
                    return sceneColor;
                }

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float distToCamera = distance(worldPos, _WorldSpaceCameraPos);

                // Та же формула Exponential Squared, что и в базовом URP-тумане (Слой 1),
                // чтобы на границе действия шейдера не было заметного шва.
                float baseFogFactor = 1.0 - saturate(exp(-_FogDensity * _FogDensity
                    * distToCamera * distToCamera));

                // Ищем, насколько пиксель "расчищен" ближайшим источником света.
                // 0 = полностью в тумане, 1 = полностью расчищено (внутри радиуса источника).
                float clearAmount = 0.0;
                for (int i = 0; i < _FogLightCount; i++)
                {
                    float distToLight = distance(worldPos, _FogLightPositions[i].xyz);
                    float radius = _FogLightRadii[i];

                    float localClear = 1.0 - smoothstep(
                        radius - _FogClearRadiusPad, radius, distToLight);

                    clearAmount = max(clearAmount, localClear);
                }

                float finalFogFactor = baseFogFactor * (1.0 - clearAmount);

                half3 result = lerp(sceneColor.rgb, _FogColor.rgb, finalFogFactor);
                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
