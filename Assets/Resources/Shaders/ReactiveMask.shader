// IN-06: draws the renderers registered in ReactiveMaskSources (rain, waterfall, butterfly stardust) into the
// reactive mask, weighted by their own vertex alpha and texture alpha.
Shader "Hidden/WP/ReactiveMask"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend One One
        BlendOp Max
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            V vert(A a) { V v; v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col; return v; }
            half4 frag(V v) : SV_Target
            {
                half a = saturate(v.col.a * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv).a);
                return half4(max(a, 0.25h), 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
