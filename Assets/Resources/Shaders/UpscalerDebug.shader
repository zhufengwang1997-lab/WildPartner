// F3 overlay views of the upscaler inputs (IN-03 motion vectors, IN-06 reactive mask). Full-screen, Blitter.
Shader "Hidden/WP/UpscalerDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float _MvScale;
        ENDHLSL
        // 0: motion vectors -> direction as colour, speed as brightness (still pixels are black)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 mv = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord).xy * _MvScale;
                float len = length(mv);
                float2 n = len > 1e-5 ? mv / len : 0;
                half3 hue = half3(0.5 + 0.5 * n.x, 0.5 + 0.5 * n.y, 0.5 - 0.35 * (n.x + n.y));   // direction -> colour
                return half4(hue * saturate(len * 1.5), 1);                                  // speed -> brightness, still = black
            }
            ENDHLSL
        }
        // 1: reactive mask -> white where set, over dark
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                half m = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord).r;
                return half4(lerp(half3(0.06, 0.05, 0.12), half3(1, 0.95, 0.6), m), 1);
            }
            ENDHLSL
        }
    }
}
