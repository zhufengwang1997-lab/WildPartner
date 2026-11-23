// WildPartner/Toon — the one opaque shader used by terrain props, vegetation, buildings and creatures
// (FR-RD-04: few shaders so the GPU Resident Drawer can batch). Stylised lighting:
//   * N.L quantised into _RampSteps bands with a soft edge, shadows tinted towards _ShadowColor (blue/purple)
//   * optional "sphere normals" for foliage blobs (normal bent towards the object pivot -> soft round canopies)
//   * optional wind (_WIND) driven by vertex colour R; the MotionVectors pass evaluates the same wind with
//     last frame's time so swaying foliage writes correct motion vectors (FR-G07 / IN-03).
Shader "WildPartner/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Tint", Color) = (0.47, 0.52, 0.76, 1)
        _RimColor ("Rim (rgb) strength (a)", Color) = (1, 0.93, 0.8, 0.15)
        _RimPower ("Rim Power", Range(1, 8)) = 4
        _RimAmbient ("Rim in Shade (sky light)", Range(0, 2)) = 0
        _RampSteps ("Ramp Steps", Range(1, 5)) = 3
        _RampSmooth ("Ramp Edge Softness", Range(0.001, 0.5)) = 0.06
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.75
        _SphereNormal ("Sphere Normal Blend (foliage)", Range(0, 1)) = 0
        _Translucency ("Back-light Translucency", Range(0, 1)) = 0
        _Emission ("Emission (x base colour)", Range(0, 8)) = 0
        _EmissionTint ("Emission Tint", Color) = (1, 1, 1, 1)
        _UpNormal ("Up Normal (grass)", Range(0, 1)) = 0
        [Toggle(_WIND)] _Wind ("Wind", Float) = 0
        _WindStrength ("Wind Strength (m)", Range(0, 1)) = 0.15
        _WindFrequency ("Wind Frequency", Range(0.1, 4)) = 1.2
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "ToonCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                half4  color      : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 posOS = ApplyWindOS(v.positionOS.xyz, v.color.r, _WP_WindTime);
                float3 nOS = normalize(lerp(v.normalOS, normalize(v.positionOS.xyz - float3(0, v.color.g * 4.0, 0) + 1e-4), _SphereNormal));
                nOS = normalize(lerp(nOS, TransformWorldToObjectDir(float3(0, 1, 0)), _UpNormal));
                VertexPositionInputs p = GetVertexPositionInputs(posOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            float _WP_RimOff;   // global: 1 indoors (EnvController)
            half Ramp(half x)
            {
                // Quantise 0..1 into _RampSteps flat bands with a softened edge between bands.
                half steps = max(_RampSteps, 1);
                half s = x * steps;
                half band = floor(s);
                half f = smoothstep(0.5 - _RampSmooth * steps, 0.5 + _RampSmooth * steps, frac(s));
                return saturate((band + f) / steps);
            }

            half4 frag(Varyings i, bool facing : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                clip(albedo.a - _Cutoff);
                #endif
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                NearFade(i.positionCS);

                half3 n = SafeNormalize(i.normalWS) * ((facing || _UpNormal > 0.5) ? 1 : -1);   // 1123e-1: a zero normal must not become NaN
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light main = GetMainLight(shadowCoord);
                half ndl = dot(n, main.direction) * 0.5 + 0.5;            // half-lambert
                half shadow = lerp(1, main.shadowAttenuation, _ShadowStrength);
                half lit = Ramp(saturate(ndl * 1.25 - 0.15)) * shadow;

                // Lit areas get the sun colour; unlit areas get the cool tint instead of black.
                half3 sun = main.color * main.distanceAttenuation;
                half3 ambient = SampleSH(n);
                half3 shade = _ShadowColor.rgb * (ambient + 0.15);
                half3 col = albedo.rgb * lerp(shade, sun + ambient * 0.35, lit);

                // Foliage glows a little when the sun is behind it.
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half back = saturate(dot(v, -main.direction));
                col += albedo.rgb * sun * pow(back, 4) * _Translucency * shadow;

                // Rim light, strongest on the lit side.
                // Characters and pets (画面整改 6) also get a soft sky-coloured rim on the shaded side, which
                // separates them from busy grass and foliage behind them.
                // 1123c-A3: no rim indoors (the sun-coloured edge glint read as white lines on the furniture)
                half rim = pow(1 - saturate(dot(n, v)), _RimPower) * _RimColor.a * (1 - _WP_RimOff);
                col += _RimColor.rgb * rim * ((0.3 + 0.7 * lit) * sun + _RimAmbient * (ambient * 1.4 + 0.12));

                // Point/spot lights (lamps, campfire): simple banded add.
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS, half4(1,1,1,1));
                    col += albedo.rgb * l.color * l.distanceAttenuation * l.shadowAttenuation * Ramp(saturate(dot(n, l.direction)));
                }
                #endif
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    col += albedo.rgb * l.color * l.distanceAttenuation * l.shadowAttenuation * Ramp(saturate(dot(n, l.direction) * 0.5 + 0.5));
                LIGHT_LOOP_END
                #endif

                col += albedo.rgb * _Emission * _EmissionTint.rgb;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 posWS = TransformObjectToWorld(ApplyWindOS(v.positionOS.xyz, v.color.r, _WP_WindTime));
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - posWS);
                #else
                float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, ld));
                o.positionCS = ApplyShadowClamping(cs);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(ApplyWindOS(v.positionOS.xyz, v.color.r, _WP_WindTime));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                NearFade(i.positionCS);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(ApplyWindOS(v.positionOS.xyz, v.color.r, _WP_WindTime));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                NearFade(i.positionCS);
                return half4(SafeNormalize(i.normalWS), 0);
            }
            ENDHLSL
        }

        // Per-object motion vectors. Wind is evaluated twice: this frame (current matrix, _WP_WindTime) and
        // last frame (previous matrix, _WP_WindTimePrev), so the difference is the true screen motion.
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS  : POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionOld : TEXCOORD4;   // skinned meshes: last frame's skinned position
                half4  color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 curCS      : TEXCOORD1;
                float4 prevCS     : TEXCOORD2;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 cur = ApplyWindOS(v.positionOS.xyz, v.color.r, _WP_WindTime);
                float3 prevBase = (unity_MotionVectorsParams.x == 1) ? v.positionOld : v.positionOS.xyz;
                float3 prev = ApplyWindOSPrev(prevBase, v.color.r, _WP_WindTimePrev);
                o.positionCS = TransformObjectToHClip(cur);
                o.curCS = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, float4(cur, 1)));
                o.prevCS = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, float4(prev, 1)));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(i.positionCS);
                #endif
                NearFade(i.positionCS);
                return float4(CalcNdcMotionVectorFromCsPositions(i.curCS, i.prevCS), 0, 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
