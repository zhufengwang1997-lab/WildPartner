// WildPartner/ToonTerrain — 4-layer terrain with the same banded lighting / cool shadow tint as
// WildPartner/Toon, so ground and props read as one style. Unity binds _Control and _Splat0..3 (+ _ST)
// from the TerrainLayers automatically. A low-frequency colour variation breaks up tiling.
Shader "WildPartner/ToonTerrain"
{
    Properties
    {
        [HideInInspector] _Control ("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat0 ("Layer 0", 2D) = "white" {}
        [HideInInspector] _Splat1 ("Layer 1", 2D) = "white" {}
        [HideInInspector] _Splat2 ("Layer 2", 2D) = "white" {}
        [HideInInspector] _Splat3 ("Layer 3", 2D) = "white" {}
        _ShadowColor ("Shadow Tint", Color) = (0.47, 0.53, 0.74, 1)
        _RampSteps ("Ramp Steps", Range(1, 5)) = 3
        _RampSmooth ("Ramp Softness", Range(0.001, 0.5)) = 0.08
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.8
        _Variation ("Macro Variation", Range(0, 1)) = 0.25
        [NoScaleOffset] _GroundTint ("Ground Tint (0.5 = neutral, linear)", 2D) = "gray" {}
        _WorldSize ("World Size (m)", Float) = 1024
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-100" "TerrainCompatible" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Control); SAMPLER(sampler_Control);
        TEXTURE2D(_Splat0); SAMPLER(sampler_Splat0);
        TEXTURE2D(_Splat1); TEXTURE2D(_Splat2); TEXTURE2D(_Splat3);
        TEXTURE2D(_GroundTint); SAMPLER(sampler_GroundTint);
        TEXTURE2D(_WP_GrassMap);   // global, set by GrassRenderer (R = grass density)
        CBUFFER_START(UnityPerMaterial)
            float4 _Control_ST, _Control_TexelSize;
            float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
            half4 _ShadowColor;
            half _RampSteps, _RampSmooth, _ShadowStrength, _Variation;
            float _WorldSize;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half3 normalWS : TEXCOORD2; half fog : TEXCOORD3; };

            V vert(A v)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Ramp(half x)
            {
                half steps = max(_RampSteps, 1);
                half s = x * steps;
                half f = smoothstep(0.5 - _RampSmooth * steps, 0.5 + _RampSmooth * steps, frac(s));
                return saturate((floor(s) + f) / steps);
            }

            float n2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(n2(i), n2(i + float2(1, 0)), f.x), lerp(n2(i + float2(0, 1)), n2(i + 1), f.x), f.y); }

            half4 frag(V i) : SV_Target
            {
                half4 ctrl = SAMPLE_TEXTURE2D(_Control, sampler_Control, i.uv);
                float2 wuv = i.positionWS.xz;
                half3 c0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, i.uv * _Splat0_ST.xy + _Splat0_ST.zw).rgb;
                half3 c1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, i.uv * _Splat1_ST.xy + _Splat1_ST.zw).rgb;
                half3 c2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat0, i.uv * _Splat2_ST.xy + _Splat2_ST.zw).rgb;
                half3 c3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat0, i.uv * _Splat3_ST.xy + _Splat3_ST.zw).rgb;
                half3 albedo = (c0 * ctrl.r + c1 * ctrl.g + c2 * ctrl.b + c3 * ctrl.a) / max(dot(ctrl, 1), 1e-3);
                // hand-painted macro colour shared with the grass (sun / shade patches, forest floor, tree shade)
                half3 tint = SAMPLE_TEXTURE2D(_GroundTint, sampler_GroundTint, wuv / _WorldSize).rgb * 2.0;
                albedo *= lerp(half3(1, 1, 1), tint, 0.85);
                float macro = vn(wuv * 0.05) * 0.6 + vn(wuv * 0.2) * 0.4;
                albedo *= lerp(1 - _Variation * 0.3, 1 + _Variation * 0.3, macro);
                // under the grass the ground takes the colour of the lower blades near the camera and of the distant
                // lawn further out, so gaps between thin blades read as more grass and the far edge has no seam (试玩反馈 5)
                half dens = SAMPLE_TEXTURE2D(_WP_GrassMap, sampler_GroundTint, wuv / _WorldSize).r;
                float camD = distance(i.positionWS.xz, _WorldSpaceCameraPos.xz);
                half3 under = lerp(half3(0.15, 0.31, 0.07), half3(0.33, 0.53, 0.15), smoothstep(6, 45, camD)) * tint;   // flat ground is lit brighter than blades: keep it a shade darker
                albedo = lerp(albedo, under, saturate(dens * 1.3) * 0.9);

                half3 n = normalize(i.normalWS);
                // steep slopes become painted rock (画面整改 2/cliff): warm grey with horizontal strata and lighter
                // ledges, instead of grass colour stretched over a cliff (read as one flat purple sheet in shade)
                float steep = smoothstep(0.66, 0.42, n.y);
                if (steep > 0.001)
                {
                    float3 pw = i.positionWS;
                    float along = dot(pw.xz, normalize(float2(-n.z, n.x) + 1e-4));
                    float strata = vn(float2(along * 0.08, pw.y * 0.9)) * 0.6 + vn(float2(along * 0.3, pw.y * 2.7)) * 0.4;
                    float ledge = smoothstep(0.55, 0.7, vn(float2(along * 0.05, pw.y * 0.35)));
                    half3 rock = lerp(half3(0.36, 0.34, 0.37), half3(0.56, 0.50, 0.45), strata);
                    rock = lerp(rock, half3(0.42, 0.55, 0.30), ledge * 0.6);   // moss on the ledges
                    albedo = lerp(albedo, rock, steep);
                }
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, main.direction) * 0.5 + 0.5;
                half shadow = lerp(1, main.shadowAttenuation, _ShadowStrength);
                // mostly smooth: a hard toon ramp on rolling terrain draws contour-line "terraces"
                half soft = saturate(ndl * 1.25 - 0.15);
                half lit = lerp(soft, Ramp(soft), 0.2) * shadow;
                half3 ambient = SampleSH(n);
                half3 col = albedo * lerp(_ShadowColor.rgb * (ambient + 0.15), main.color * main.distanceAttenuation + ambient * 0.35, lit);

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    col += albedo * l.color * l.distanceAttenuation * l.shadowAttenuation * Ramp(saturate(dot(n, l.direction) * 0.5 + 0.5));
                LIGHT_LOOP_END
                #endif

                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(A v) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection)));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; };
            float4 vert(A v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; half3 n : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.n = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(V i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
