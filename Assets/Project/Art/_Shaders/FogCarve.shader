Shader "Hidden/FogCarve"
{
    Properties
    {
        _FogColor("Fog Color", Color) = (0.04, 0.047, 0.078, 1)
        _FogOpacity("Fog Opacity Outside Lit Zone", Range(0.0, 1.0)) = 1.0
        _FogClearRadiusPad("Light Edge Softness (m)", Range(0.1, 10)) = 2.0

        // Максимальная дистанция проявления источника = его Radius * этот множитель.
        // Источник радиусом 8 при множителе 3 виден издалека с 24м, источник радиусом
        // 20 (например, большой костёр) — уже с 60м. Так не приходится вручную подбирать
        // отдельную дистанцию на каждый тип источника — она сама масштабируется от яркости.
        _RevealDistanceMultiplier("Reveal Distance Multiplier (x Radius)", Range(1, 10)) = 3

        // Ширина плавного перехода (в метрах) на границе дистанции проявления —
        // раньше источник появлялся мгновенно (жёсткий if), теперь плавно "проступает".
        _RevealTransition("Reveal Transition Width (m)", Range(0.5, 30)) = 10
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

            float4 _FogLightPositions[MAX_FOG_LIGHTS];
            float _FogLightRadii[MAX_FOG_LIGHTS];
            int _FogLightCount;

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float _FogOpacity;
                float _FogClearRadiusPad;
                float _RevealDistanceMultiplier;
                float _RevealTransition;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                if (rawDepth <= 0.0001)
                {
                    return sceneColor;
                }

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float clearAmount = 0.0;
                for (int i = 0; i < _FogLightCount; i++)
                {
                    float radius = _FogLightRadii[i];

                    // Дистанция проявления масштабируется от радиуса ЭТОГО источника —
                    // так у каждого источника своя "дальность видимости" пропорционально его яркости.
                    float maxRevealDistance = radius * _RevealDistanceMultiplier;
                    float edge1 = maxRevealDistance;
                    float edge0 = max(0.0, edge1 - _RevealTransition);
                    // Защита от вырожденного случая (очень маленький радиус + большой Transition),
                    // когда edge0 мог бы схлопнуться с edge1 или превысить его — smoothstep
                    // с edge0 >= edge1 даёт некорректный результат.
                    edge1 = max(edge1, edge0 + 0.001);

                    float distCameraToLight = distance(_WorldSpaceCameraPos, _FogLightPositions[i].xyz);
                    // 1 = источник в пределах дальности проявления, 0 = слишком далеко от камеры,
                    // плавный градиент между ними — вместо прежнего резкого if/continue.
                    float revealFactor = 1.0 - smoothstep(edge0, edge1, distCameraToLight);

                    float distToLight = distance(worldPos, _FogLightPositions[i].xyz);
                    float localClear = 1.0 - smoothstep(
                        radius - _FogClearRadiusPad, radius, distToLight);

                    clearAmount = max(clearAmount, localClear * revealFactor);
                }

                float finalFogFactor = saturate(1.0 - clearAmount) * _FogOpacity;

                half3 result = lerp(sceneColor.rgb, _FogColor.rgb, finalFogFactor);
                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}