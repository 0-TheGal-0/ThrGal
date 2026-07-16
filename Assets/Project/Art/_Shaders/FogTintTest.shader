Shader "Hidden/FogTintTest"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "FogTintTestPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Blit.hlsl даёт готовый вершинный шейдер Vert(), структуры Attributes/Varyings
            // и текстуру _BlitTexture с семплером sampler_LinearClamp — это стандартный способ
            // получить "то, что уже отрендерено на экране" внутри Full Screen Pass Renderer Feature.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);

                // ВРЕМЕННО: подмешиваем красный на 30%, чтобы визуально убедиться,
                // что проход реально что-то рисует. Уберём, когда перейдём к логике тумана.
                return lerp(sceneColor, half4(1, 0, 0, 1), 0.3);
            }
            ENDHLSL
        }
    }
}
