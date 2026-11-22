// WildPartner/Grass — dense short grass drawn by GrassRenderer with Graphics.RenderMeshPrimitives, one draw per
// 8 m chunk around the camera. Nothing per blade is stored on the CPU: every instance is one clump on a
// world-snapped cell grid (so clumps never swim as the camera moves); its jitter, yaw and size come from a
// hash of the cell, its height from the terrain height texture, and its density / height / dryness from the
// baked grass map. Colour runs from the ground tint at the root to a bright yellow-green tip, so the lawn
// melts into the terrain at distance where the blades shrink to nothing.
Shader "WildPartner/Grass"
{
    Properties
    {
        _BaseColor ("Grass Colour", Color) = (0.27, 0.46, 0.14, 1)
        _TipColor ("Tip Colour", Color) = (0.55, 0.76, 0.24, 1)
        _DryColor ("Dry Tip Colour", Color) = (0.82, 0.76, 0.36, 1)
        _ShadowColor ("Shadow Tint", Color) = (0.42, 0.45, 0.75, 1)
        _HeightRange ("Blade Height min/max (m)", Vector) = (0.15, 0.35, 0, 0)
        _Fade ("Fade start/end (m)", Vector) = (48, 66, 0, 0)
        _Translucency ("Back-light", Range(0, 1)) = 0.45
        _WindStrength ("Wind Bend (m per m)", Range(0, 1)) = 0.35
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_WP_GrassHeight);
        TEXTURE2D(_WP_GrassMap);
        TEXTURE2D(_WP_GroundTint);
        SAMPLER(sampler_linear_clamp);
        float4 _WP_TerrainSize;    // x = size (m), y = height scale (m), z = height texture res, w = terrain y
        float4 _WP_PlayerPos;      // xyz, w = push radius
        float4 _WP_Wind;
        float _WP_WindTime, _WP_WindTimePrev;

        // per draw (MaterialPropertyBlock): xy = chunk origin, z = cell size, w = cells per side
        float4 _Chunk;
        float _BladeWidth;         // LOD widening

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _TipColor, _DryColor, _ShadowColor;
            float4 _HeightRange, _Fade;
            half _Translucency, _WindStrength, _ShadowStrength;
        CBUFFER_END

        struct Attributes
        {
            float3 pos : POSITION;    // xz = blade base offset in cells, y = height fraction t
            float3 side : NORMAL;     // xz = blade side vector, y = lean
            float2 uv : TEXCOORD0;    // x = half-width offset (m, signed), y = t
            float2 rnd : TEXCOORD1;   // x = density threshold, y = height variation
            uint id : SV_InstanceID;
        };

        float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

        float TerrainHeight(float2 xz)
        {
            float res = _WP_TerrainSize.z;
            float2 uv = xz / _WP_TerrainSize.x * ((res - 1) / res) + 0.5 / res;
            return SAMPLE_TEXTURE2D_LOD(_WP_GrassHeight, sampler_linear_clamp, uv, 0).r * _WP_TerrainSize.y + _WP_TerrainSize.w;
        }

        struct Blade { float3 positionWS; float t; half4 map; half3 tint; half rnd; };

        Blade BuildBlade(Attributes v, float time)
        {
            Blade b;
            uint n = (uint)_Chunk.w;
            float2 cellPos = _Chunk.xy + float2(v.id % n, v.id / n) * _Chunk.z;
            float h1 = Hash(cellPos), h2 = Hash(cellPos + 17.17), h3 = Hash(cellPos + 3.71);
            float2 centre = cellPos + float2(h1, h2) * _Chunk.z;
            float a = h3 * 6.2831853;
            float2x2 rot = float2x2(cos(a), -sin(a), sin(a), cos(a));
            float2 baseXZ = centre + mul(rot, v.pos.xz) * _Chunk.z;
            float2 sideXZ = mul(rot, v.side.xz);

            float2 tuv = baseXZ / _WP_TerrainSize.x;
            half4 map = SAMPLE_TEXTURE2D_LOD(_WP_GrassMap, sampler_linear_clamp, tuv, 0);
            half3 tint = SAMPLE_TEXTURE2D_LOD(_WP_GroundTint, sampler_linear_clamp, tuv, 0).rgb;

            float dist = distance(baseXZ, _WorldSpaceCameraPos.xz);
            float fade = 1 - smoothstep(_Fade.x, _Fade.y, dist);
            float grow = saturate((map.r - v.rnd.x) * 6.0);            // thin out where the map is sparse
            float height = lerp(_HeightRange.x, _HeightRange.y, v.rnd.y * 0.6 + h1 * 0.4) * (0.55 + map.g * 0.9) * grow * fade;

            float t = v.pos.y;
            float3 basePos = float3(baseXZ.x, TerrainHeight(baseXZ), baseXZ.y);
            float2 facing = float2(-sideXZ.y, sideXZ.x);
            float bend = t * t;

            // wind: slow gusts rolling across the field + fast flutter
            float2 wd = _WP_Wind.xy;
            float gust = sin(time * 1.3 - dot(baseXZ, wd) * 0.18) * 0.5 + 0.5;
            gust = gust * gust * (0.6 + 0.4 * sin(time * 0.37 + dot(baseXZ, float2(0.05, 0.03))));
            float flutter = sin(time * 4.7 + h2 * 20 + baseXZ.x * 0.7) * 0.12;
            float2 windOff = wd * ((gust + 0.25 + flutter) * _WindStrength * _WP_Wind.z) * bend * height;

            // the witch (and her pets) push the blades aside
            float2 dp = baseXZ - _WP_PlayerPos.xz;
            float pd = length(dp);
            float push = saturate(1 - pd / max(_WP_PlayerPos.w, 0.01)) * step(abs(basePos.y - _WP_PlayerPos.y), 1.5);
            float2 pushOff = (pd > 1e-3 ? dp / pd : 0) * push * 0.8 * bend * height;

            float w = v.uv.x * _BladeWidth * (1 - t * 0.85);
            float3 p = basePos;
            p.xz += sideXZ * w + facing * v.side.y * bend * height + windOff + pushOff;
            p.y += t * height * (1 - push * 0.45);

            b.positionWS = p; b.t = t; b.map = map; b.tint = tint; b.rnd = h1;
            return b;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : TEXCOORD1;    // rgb albedo, a = t
                half fog : TEXCOORD2;
            };

            V vert(Attributes v)
            {
                Blade b = BuildBlade(v, _WP_WindTime);
                V o;
                o.positionWS = b.positionWS;
                o.positionCS = TransformWorldToHClip(b.positionWS);
                half3 ground = b.tint * 2.0;                                  // 0.5 = neutral
                half3 root = _BaseColor.rgb * ground * 0.62;
                half3 tip = lerp(_TipColor.rgb, _DryColor.rgb, saturate(b.map.b * 0.7 + (b.rnd - 0.5) * 0.2)) * ground;
                tip *= 0.88 + b.rnd * 0.24;
                o.color = half4(lerp(root, tip, smoothstep(0, 1, b.t)), b.t);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 albedo = i.color.rgb;
                half3 n = half3(0, 1, 0);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = lerp(1, main.shadowAttenuation, _ShadowStrength);
                half ndl = saturate(main.direction.y * 0.6 + 0.55);
                half lit = ndl * shadow;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                half occ = ao.indirectAmbientOcclusion;
                #else
                half occ = 1;
                #endif
                occ *= lerp(0.65, 1, i.color.a);                               // self-shadowed roots
                half3 ambient = SampleSH(n) * occ;
                half3 col = albedo * lerp(_ShadowColor.rgb * (ambient + 0.12), main.color + ambient * 0.35, lit);
                // light glowing through the blade tips when looking towards the sun
                half3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half back = pow(saturate(dot(-vdir, main.direction)), 3) * i.color.a;
                col += albedo * main.color * back * _Translucency * shadow;

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    col += albedo * l.color * l.distanceAttenuation * l.shadowAttenuation * 0.8;
                LIGHT_LOOP_END
                #endif
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(Attributes v) : SV_POSITION { return TransformWorldToHClip(BuildBlade(v, _WP_WindTime).positionWS); }
            half frag(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(Attributes v) : SV_POSITION { return TransformWorldToHClip(BuildBlade(v, _WP_WindTime).positionWS); }
            half4 frag(float4 p : SV_POSITION) : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }

        // Wind-only motion: this frame's blade minus last frame's (IN-03). The grass never moves as an object.
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
            struct V { float4 positionCS : SV_POSITION; float4 cur : TEXCOORD0; float4 prev : TEXCOORD1; };
            V vert(Attributes v)
            {
                V o;
                float3 p = BuildBlade(v, _WP_WindTime).positionWS;
                float3 pp = BuildBlade(v, _WP_WindTimePrev).positionWS;
                o.positionCS = TransformWorldToHClip(p);
                o.cur = mul(_NonJitteredViewProjMatrix, float4(p, 1));
                o.prev = mul(_PrevViewProjMatrix, float4(pp, 1));
                return o;
            }
            float4 frag(V i) : SV_Target { return float4(CalcNdcMotionVectorFromCsPositions(i.cur, i.prev), 0, 0); }
            ENDHLSL
        }
    }
}
