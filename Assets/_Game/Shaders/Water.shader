// WildPartner/Water — stylised transparent water for the lake, river ribbon and waterfalls.
// Colour from scene-depth thickness (shallow turquoise -> deep blue), shoreline foam, flowing streaks
// along UV.y (rivers are generated with uv.y = distance along the flow), banded sun glints.
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
        _GlintAmount ("Glint", Range(0, 2)) = 0.6
        _WaveScale ("Wave Scale", Float) = 0.15
        _Reflect ("Sky Reflection", Range(0, 1)) = 0.7
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

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthRange, _FoamWidth, _FlowSpeed, _StreakDensity, _StreakAmount, _GlintAmount, _WaveScale, _Reflect;
            CBUFFER_END

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

            half4 frag(Varyings i) : SV_Target
            {
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surfZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float thick = max(sceneZ - surfZ, 0);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // view-ray thickness -> approximate vertical depth, so grazing views don't turn the shallows deep
                float vdepth = thick * saturate(abs(v.y) + 0.15);
                half depthT = saturate(pow(saturate(vdepth / _DepthRange), 0.7));
                half4 col = lerp(_ShallowColor, _DeepColor, depthT);
                col.a = lerp(_ShallowColor.a * 0.7, _DeepColor.a, smoothstep(0, 0.6, depthT));   // see the bed near the shore

                // flow: streaks along the river (uv.y = distance downstream) + slow drifting ripples on the lake
                float t = _Time.y;
                float2 flowUV = float2(i.uv.x * 6, i.uv.y * _StreakDensity - t * _FlowSpeed);
                float streak = smoothstep(0.6, 0.8, vnoise(flowUV * float2(1, 0.25)) * vnoise(flowUV * 2.1 + 3.7));
                float2 wuv = i.positionWS.xz * 0.15;
                float ripple = vnoise(wuv + t * 0.05) * vnoise(wuv * 1.7 - t * 0.04);
                float ripple2 = vnoise(wuv * 3.1 - float2(t * 0.11, t * 0.07));

                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 h = normalize(v + main.direction);
                float3 n = normalize(float3((ripple - 0.25) * 0.5 + (ripple2 - 0.5) * 0.18, 1, (streak - 0.2) * 0.4 + (ripple2 - 0.5) * 0.12));
                half spec = step(0.985, dot(n, h)) * _GlintAmount;

                col.rgb *= main.color * (0.55 + 0.45 * main.shadowAttenuation) + SampleSH(float3(0, 1, 0)) * 0.4;

                // sky reflection (fresnel): the lake picks up the sky gradient, strongest at grazing angles
                float3 r = reflect(-v, n);
                half3 sky = lerp(_WP_SkyHorizon.rgb, _WP_SkyZenith.rgb, saturate(r.y * 1.6));
                half fres = 0.06 + 0.94 * pow(1 - saturate(dot(n, v)), 5);
                col.rgb = lerp(col.rgb, sky, saturate(fres * 1.6) * _Reflect);
                col.a = saturate(col.a + fres * _Reflect * 0.5);

                col.rgb += _StreakAmount * streak + spec * main.color;

                // shore foam: a thin broken line right at the waterline plus softer patches just behind it
                float fn = vnoise(wuv * 9 + float2(t * 0.25, -t * 0.18));
                half edgeLine = 1 - smoothstep(0.02, 0.12, vdepth + (fn - 0.5) * 0.06);
                half band = (1 - smoothstep(0, _FoamWidth, vdepth)) * smoothstep(0.55, 0.75, fn);
                half foam = saturate(edgeLine + band * 0.7);
                col = lerp(col, _FoamColor, foam * 0.85);
                col.a = saturate(col.a + foam + streak * _StreakAmount);
                return col;
            }
            ENDHLSL
        }
    }
}
