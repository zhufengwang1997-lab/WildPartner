// WildPartner/Water — stylised water for the lake and the river ribbon (试玩反馈 1123c-C2 rewrite).
//  * two scrolling normal maps (broad swell A + fine chop B); on the river both run downstream (uv.y = metres/8
//    along the flow, uv.x 0..1 across), with flow-aligned streaks on top; on the lake they drift with the wind
//  * colour by water depth (scene depth): turquoise shallows -> deep blue, the bed seen through refraction
//    (_CameraOpaqueTexture, offset by the normals, fading out where the water is thin)
//  * Fresnel reflection: analytic sky gradient (follows the time of day) mixed with the URP reflection probe
//    (WaterProbe: a realtime probe re-rendered as the time of day / weather change) — see DECISIONS
//  * sharp sun glints, shore foam, and interactive ripple rings (_WP_Ripples, written by PlayerController)
Shader "WildPartner/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.35, 0.85, 0.85, 0.55)
        _DeepColor ("Deep", Color) = (0.08, 0.36, 0.72, 0.92)
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _DepthRange ("Depth Range (m)", Float) = 3
        _FoamWidth ("Shore Foam Width (m)", Float) = 0.6
        _FlowSpeed ("Flow Speed", Float) = 0.4
        _StreakDensity ("Streak Density", Float) = 1
        _StreakAmount ("Streak Amount", Range(0, 1)) = 0.25
        _GlintAmount ("Glint", Range(0, 4)) = 1.5
        _WaveScale ("Wave Scale", Float) = 0.15
        _Reflect ("Reflection", Range(0, 1)) = 0.7
        [NoScaleOffset] _NormalA ("Normal A (swell)", 2D) = "bump" {}
        [NoScaleOffset] _NormalB ("Normal B (chop)", 2D) = "bump" {}
        _NormalTileA ("Normal A size (m)", Float) = 9
        _NormalTileB ("Normal B size (m)", Float) = 2.6
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _Refraction ("Refraction", Range(0, 0.1)) = 0.035
        _River ("River (flow along uv)", Float) = 0
        _ProbeMix ("Probe vs sky", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthRange, _FoamWidth, _FlowSpeed, _StreakDensity, _StreakAmount, _GlintAmount, _WaveScale, _Reflect;
                float _NormalTileA, _NormalTileB, _NormalStrength, _Refraction, _River, _ProbeMix;
            CBUFFER_END
            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 _WP_SkyZenith, _WP_SkyHorizon;
            float4 _WP_Ripples[8];   // xyz = centre, w = start time (Time.timeSinceLevelLoad); w < 0 = unused
            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + 1), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws.y += (sin(ws.x * 0.35 + _Time.y * 1.3) + sin(ws.z * 0.27 + _Time.y)) * _WaveScale * 0.5;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 SampleN(TEXTURE2D_PARAM(tex, smp), float2 uv) { return UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, uv)); }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // ---- normals: two scrolling maps, in "flow space" (metres; y = downstream on the river) ----
                float2 p, flowDir;
                if (_River > 0.5) { p = float2(i.uv.x * 6.0, i.uv.y * 8.0); flowDir = float2(0, 1); }
                else { p = i.positionWS.xz; flowDir = normalize(float2(0.8, 0.6)); }
                float sp = _River > 0.5 ? _FlowSpeed * 2.2 : 0.18;
                float2 uvA = p / _NormalTileA + flowDir * t * sp / _NormalTileA;
                float2 uvB = p / _NormalTileB + float2(flowDir.y, -flowDir.x) * 0.25 * t * 0.3 / _NormalTileB + flowDir * t * sp * 1.6 / _NormalTileB;
                if (_River > 0.5) { uvA.x *= 0.55; uvB.x *= 0.7; }   // stretched along the flow
                half3 nA = SampleN(TEXTURE2D_ARGS(_NormalA, sampler_NormalA), uvA);
                half3 nB = SampleN(TEXTURE2D_ARGS(_NormalB, sampler_NormalB), uvB);
                half3 nB2 = SampleN(TEXTURE2D_ARGS(_NormalB, sampler_NormalB), uvB * 0.61 + 0.37 - flowDir * t * sp * 0.4 / _NormalTileB);
                float2 slope = (nA.xy * 1.0 + nB.xy * 0.7 + nB2.xy * 0.45) * _NormalStrength;
                // on the river the texture space is along the ribbon; uv derivatives turn it into world space
                if (_River > 0.5)
                {
                    float3 dpx = ddx(i.positionWS), dpy = ddy(i.positionWS);
                    float2 dux = ddx(p), duy = ddy(p);
                    float det = dux.x * duy.y - dux.y * duy.x;
                    float3 T = (dpx * duy.y - dpy * dux.y) / (det + 1e-6), B = (dpy * dux.x - dpx * duy.x) / (det + 1e-6);
                    float2 tw = normalize(T.xz + 1e-5), bw = normalize(B.xz + 1e-5);
                    slope = slope.x * tw + slope.y * bw;
                }
                // interactive ripples: rings spreading from where she moves in the water
                float ring = 0;
                for (int k = 0; k < 8; k++)
                {
                    float4 rp = _WP_Ripples[k];
                    float age = _Time.y - rp.w;
                    if (rp.w < 0 || age > 2.5 || age < 0) continue;
                    float2 d = i.positionWS.xz - rp.xz;
                    float r = length(d), front = age * 1.1 + 0.15;
                    float w = exp(-pow((r - front) / 0.12, 2)) * (1 - age / 2.5);
                    slope += d / max(r, 1e-3) * w * 0.9 * cos((r - front) * 18);
                    ring += w;
                }
                float3 n = normalize(float3(slope.x, 1, slope.y));

                // ---- depth, refraction ----
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surfZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float thick = max(sceneZ - surfZ, 0);
                float vdepth = thick * saturate(abs(v.y) + 0.15);
                float2 ruv = suv + slope * _Refraction * saturate(vdepth * 0.8);
                // don't pull in things in front of the water
                float rz = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (rz < surfZ) ruv = suv;
                half3 bed = SampleSceneColor(ruv);
                half depthT = saturate(pow(saturate(vdepth / _DepthRange), 0.7));
                half3 tint = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                // light absorbed with depth: the bed fades into the water colour
                half3 body = lerp(bed * lerp(half3(1, 1, 1), tint * 1.6, 0.55), tint, saturate(depthT * 1.15 + 0.12));

                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 amb = SampleSH(float3(0, 1, 0));
                half3 lit = main.color * (0.55 + 0.45 * main.shadowAttenuation) + amb * 0.4;
                // the water's own colour is lit; the refracted bed already is
                body = lerp(body, body * saturate(lit), depthT);

                // ---- reflection ----
                float3 r = reflect(-v, n);
                half3 sky = lerp(_WP_SkyHorizon.rgb, _WP_SkyZenith.rgb, saturate(r.y * 1.6));
                half3 probe = GlossyEnvironmentReflection(r, i.positionWS, 0.08h, 1.0h);   // realtime probe over the valley, refreshed with the time of day
                half3 refl = lerp(sky, probe, _ProbeMix);
                half fres = 0.04 + 0.96 * pow(1 - saturate(dot(n, v)), 5);
                half3 col = lerp(body, refl, saturate(fres * 1.5) * _Reflect);

                // ---- sun glints: sharp highlights from the normal maps, only in sunlight ----
                float3 h = normalize(v + main.direction);
                half nh = saturate(dot(n, h));
                half spec = (pow(nh, 900) * 6 + smoothstep(0.992, 0.998, nh) * 0.8) * _GlintAmount * main.shadowAttenuation;
                col += spec * main.color;

                // ---- flow streaks (river), foam ----
                float2 flowUV = float2(i.uv.x * 6, i.uv.y * _StreakDensity - t * _FlowSpeed);
                float streak = smoothstep(0.6, 0.8, vnoise(flowUV * float2(1, 0.25)) * vnoise(flowUV * 2.1 + 3.7)) * _River;
                col += _StreakAmount * streak * saturate(lit);
                float2 wuv = i.positionWS.xz * 0.15;
                float fn = vnoise(wuv * 9 + float2(t * 0.25, -t * 0.18));
                half edgeLine = 1 - smoothstep(0.02, 0.12, vdepth + (fn - 0.5) * 0.06);
                half band = (1 - smoothstep(0, _FoamWidth, vdepth)) * smoothstep(0.55, 0.75, fn);
                half foam = saturate(edgeLine + band * 0.7 + ring * 0.25);
                col = lerp(col, _FoamColor.rgb * saturate(lit + 0.15), foam * 0.85);

                // soft edge right at the shore, otherwise opaque (the bed is already in the colour)
                half a = saturate(smoothstep(0.0, 0.06, vdepth) + foam);
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
