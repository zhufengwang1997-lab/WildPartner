// WildPartner/FarRidge — distant mountain silhouettes (画面整改 5). Several concentric ridge rings around the
// valley, each with its own material: the nearer ones are darker blue-green with crisp lit/shaded faces,
// farther ones lighter, bluer and lower in contrast until the last one is little more than a pale outline
// against the sky. Peaks above the snow line get snow (warm on the sun side, lilac in shade). The base of
// every layer sinks into the horizon haze.
// Drawn after the skybox (queue 2600+, one queue per layer: farthest first) without writing depth, so the
// height-fog pass treats them as sky and only adds horizon haze, and nearer terrain still hides them.
Shader "WildPartner/FarRidge"
{
    Properties
    {
        _RockColor ("Rock", Color) = (0.30, 0.38, 0.46, 1)
        _Fade ("Haze amount", Range(0, 1)) = 0.3
        _SnowLine ("Snow line (m)", Float) = 9999
        _BaseFade ("Base haze height (m)", Vector) = (0, 80, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-400" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off ZTest LEqual Cull Off Blend Off
        Pass
        {
            Name "FarRidge"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _RockColor;
            float _Fade, _SnowLine;
            float4 _BaseFade;
            CBUFFER_END
            float4 _WP_SkyHorizon, _WP_SkyZenith, _WP_SunColor, _WP_SunDir, _WP_FogColor;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            float h1(float x) { return frac(sin(x * 127.1) * 43758.55); }
            float n1(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(h1(i), h1(i + 1), f); }

            V vert(A v)
            {
                V o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.nWS);
                float3 sun = normalize(_WP_SunDir.xyz);
                float day = saturate(dot(_WP_SunColor.rgb, 0.333) * 1.6);
                float ndl = saturate(dot(n, sun) * 0.5 + 0.5);
                float lit = smoothstep(0.3, 0.75, ndl);

                // uv.x runs around the ring in metres: streaky snow following the slopes
                float streak = n1(i.uv.x * 0.025) * 0.65 + n1(i.uv.x * 0.08) * 0.35;
                float snowLine = _SnowLine - streak * 40 + (1 - lit) * 10;
                float snow = smoothstep(snowLine - 6, snowLine + 6, i.posWS.y);

                float3 shadeTint = lerp(_WP_SkyZenith.rgb, float3(0.45, 0.48, 0.75), 0.5);
                float3 rock = _RockColor.rgb * lerp(shadeTint * 1.1, _WP_SunColor.rgb * 0.75 + 0.25, lit);
                float3 snowC = lerp(float3(0.66, 0.70, 0.92) * lerp(0.7, 1, day), lerp(1, _WP_SunColor.rgb, 0.25) * 1.05, lit);
                float3 c = lerp(rock, snowC, snow) * lerp(0.22, 1, day);

                float3 haze = lerp(_WP_SkyHorizon.rgb, _WP_FogColor.rgb, 0.35);
                float baseH = saturate(1 - (i.posWS.y - _BaseFade.x) / _BaseFade.y);
                float f = saturate(_Fade + (1 - _Fade) * baseH * baseH * 0.85);
                c = lerp(c, haze, f);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
