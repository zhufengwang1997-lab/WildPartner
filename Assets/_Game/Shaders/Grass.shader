// WildPartner/Grass — lawn grass drawn by GrassRenderer with Graphics.RenderMeshPrimitives (GPU instancing),
// one draw per 7 m chunk around the camera. One instance = one tuft on a world-snapped 0.25 m cell (tufts never
// swim as the camera moves); its jitter, yaw, size and colour come from a hash of the cell, its height from the
// terrain height texture, and density / height / dryness from the baked grass map. Blades (试玩反馈 5): 4 segments
// + tip, 1–3 cm wide, 30–70 cm tall, curving outwards with a drooping tip, bending from the root in the wind.
// Distance thins the blades in mesh order (see GrassRenderer: the LOD meshes are prefixes) and widens the
// survivors, then shrinks the last ones into the ground; colour converges on the terrain colour at the same time.
Shader "WildPartner/Grass"
{
    Properties
    {
        _BaseColor ("Root Colour", Color) = (0.13, 0.30, 0.09, 1)
        _TipColor ("Tip Colour", Color) = (0.62, 0.80, 0.27, 1)
        _DryColor ("Dry Tip Colour", Color) = (0.80, 0.76, 0.38, 1)
        _ShadowColor ("Shadow Tint", Color) = (0.42, 0.48, 0.78, 1)
        _FarColor ("Distant Lawn Colour", Color) = (0.42, 0.60, 0.22, 1)
        _HeightRange ("Blade Height min/max (m)", Vector) = (0.3, 0.7, 0, 0)
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
        float4 _WP_GrassPushers[6]; // the animals: xyz, w = push radius (0 = off)
        float4 _WP_Wind;
        float _WP_WindTime, _WP_WindTimePrev;

        // per draw (MaterialPropertyBlock): xy = chunk origin, z = cell size, w = cells per side
        float4 _Chunk;
        float4 _WP_GrassLod;       // x = LOD0 radius, y = LOD1 radius, z = LOD1 kept fraction, w = LOD2 kept fraction
        float4 _WP_GrassFade;      // x = start shrinking, y = gone

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _TipColor, _DryColor, _ShadowColor, _FarColor;
            float4 _HeightRange;
            half _Translucency, _WindStrength, _ShadowStrength;
        CBUFFER_END

        struct Attributes
        {
            float3 pos : POSITION;    // xz = blade base offset in cells, y = height fraction t
            float3 side : NORMAL;     // xz = blade side vector, y = lean
            float2 uv : TEXCOORD0;    // x = half-width offset (m, signed), y = t
            float2 rnd : TEXCOORD1;   // x = thinning threshold (blade order), y = height variation
            uint id : SV_InstanceID;
        };

        float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

        float TerrainHeight(float2 xz)
        {
            float res = _WP_TerrainSize.z;
            float2 uv = xz / _WP_TerrainSize.x * ((res - 1) / res) + 0.5 / res;
            return SAMPLE_TEXTURE2D_LOD(_WP_GrassHeight, sampler_linear_clamp, uv, 0).r * _WP_TerrainSize.y + _WP_TerrainSize.w;
        }

        /// fraction of a tuft's blades kept at this distance; continuous across the LOD radii
        float KeepAt(float d)
        {
            float k0 = lerp(1, _WP_GrassLod.z, smoothstep(_WP_GrassLod.x * 0.55, _WP_GrassLod.x - 0.4, d));
            float k1 = lerp(_WP_GrassLod.z, _WP_GrassLod.w, smoothstep(_WP_GrassLod.x, _WP_GrassLod.y - 0.4, d));
            return d < _WP_GrassLod.x ? k0 : k1;
        }

        struct Blade { float3 positionWS; float3 normalWS; float t; half4 map; half3 tint; half3 rnd; float far; };

        Blade BuildBlade(Attributes v, float time)
        {
            Blade b;
            uint n = (uint)_Chunk.w;
            float2 cellPos = _Chunk.xy + float2(v.id % n, v.id / n) * _Chunk.z;
            float h1 = Hash(cellPos), h2 = Hash(cellPos + 17.17), h3 = Hash(cellPos + 3.71);
            float2 centre = cellPos + (float2(h1, h2) * 1.5 - 0.25) * _Chunk.z;   // jitter past the cell: no visible rows
            float a = h3 * 6.2831853;
            float2x2 rot = float2x2(cos(a), -sin(a), sin(a), cos(a));
            float2 baseXZ = centre + mul(rot, v.pos.xz) * _Chunk.z;
            float2 sideXZ = mul(rot, v.side.xz);
            float2 facing = float2(-sideXZ.y, sideXZ.x);

            float2 tuv = baseXZ / _WP_TerrainSize.x;
            half4 map = SAMPLE_TEXTURE2D_LOD(_WP_GrassMap, sampler_linear_clamp, tuv, 0);
            half3 tint = SAMPLE_TEXTURE2D_LOD(_WP_GroundTint, sampler_linear_clamp, tuv, 0).rgb;

            // density: the grass map, a per-tuft fullness (8–20 blades), then distance thinning in blade order
            float dist = distance(baseXZ, _WorldSpaceCameraPos.xz);
            float keep = KeepAt(dist);
            float fill = 0.42 + 0.58 * h2;
            float grow = saturate((map.r * fill * keep - v.rnd.x) * 12.0);
            float fade = 1 - smoothstep(_WP_GrassFade.x, _WP_GrassFade.y, dist);
            float hScale = saturate(map.g * 0.8 + (h1 - 0.5) * 0.25 + 0.1);
            float height = lerp(_HeightRange.x, _HeightRange.y, hScale) * v.rnd.y * lerp(0.55, 1, saturate(map.g * 1.8)) * grow * fade;

            float t = v.pos.y;
            float lean = v.side.y * (0.8 + h3 * 0.4);
            float3 basePos = float3(baseXZ.x, TerrainHeight(baseXZ), baseXZ.y);

            // wind bends the blade from the root: offset grows with t², flutter mostly at the tip
            float2 wd = _WP_Wind.xy;
            float gust = sin(time * 1.3 - dot(baseXZ, wd) * 0.18) * 0.5 + 0.5;
            gust = gust * gust * (0.6 + 0.4 * sin(time * 0.37 + dot(baseXZ, float2(0.05, 0.03))));
            float flutter = sin(time * 4.7 + h2 * 20 + v.rnd.x * 9 + baseXZ.x * 0.7) * 0.15 * t;
            float2 windOff = wd * ((gust + 0.25 + flutter) * _WindStrength * _WP_Wind.z) * t * t * height;

            // the witch (and her pets) push the blades aside
            float2 dp = baseXZ - _WP_PlayerPos.xz;
            float pd = length(dp);
            float push = saturate(1 - pd / max(_WP_PlayerPos.w, 0.01)) * step(abs(basePos.y - _WP_PlayerPos.y), 1.5);
            float2 pushOff = (pd > 1e-3 ? dp / pd : 0) * push * 0.8 * t * t * height;
            for (int k = 0; k < 6; k++)
            {
                float4 q = _WP_GrassPushers[k];
                float2 dq = baseXZ - q.xz;
                float qd = length(dq);
                float pq = saturate(1 - qd / max(q.w, 0.01)) * step(abs(basePos.y - q.y), 1.2) * step(0.01, q.w);
                pushOff += (qd > 1e-3 ? dq / qd : 0) * pq * 0.9 * t * t * height;
                push = max(push, pq);
            }

            // shape: curve outwards (t²) and droop the tip (t³); taper to a point. Survivors of the distance
            // thinning get wider (coverage stays the same), and no blade gets thinner than ~1.3 px.
            float droop = lean * 0.5;
            float px = dist * 0.0011;
            float wScale = max(rsqrt(max(keep, 0.05)), px / max(abs(v.uv.x), 1e-4));
            float w = v.uv.x * min(wScale, 6.0) * pow(saturate(1 - t), 0.75) * (0.85 + 0.3 * h1);
            float3 p = basePos;
            p.xz += sideXZ * w + facing * (lean * t * t * height) + windOff + pushOff;
            p.y += height * (t - droop * t * t * t) * (1 - push * 0.45);

            // blade normal: facing direction tipped up by the curve (soft-shaded later)
            float3 nrm = normalize(float3(-facing.x, 0.6 + lean * t, -facing.y));

            b.positionWS = p; b.normalWS = nrm; b.t = t; b.map = map; b.tint = tint; b.rnd = half3(h1, h2, h3);
            b.far = smoothstep(_WP_GrassLod.x, _WP_GrassFade.y, dist);
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
                half3 normalWS : TEXCOORD3;
            };

            V vert(Attributes v)
            {
                Blade b = BuildBlade(v, _WP_WindTime);
                V o;
                o.positionWS = b.positionWS;
                o.positionCS = TransformWorldToHClip(b.positionWS);
                o.normalWS = b.normalWS;
                half3 ground = b.tint * 2.0;                                  // 0.5 = neutral (shared with the terrain)
                // per-tuft variation: a little hue (yellower / bluer) and brightness
                half3 vary = half3(1 + (b.rnd.x - 0.5) * 0.16, 1, 1 - (b.rnd.x - 0.5) * 0.2) * (0.9 + b.rnd.z * 0.2);
                half3 root = _BaseColor.rgb * ground;
                half3 tip = lerp(_TipColor.rgb, _DryColor.rgb, saturate(b.map.b * 0.5 + (b.rnd.y - 0.5) * 0.2)) * ground * vary;
                half3 col = lerp(root, tip, pow(b.t, 0.8));
                col = lerp(col, _FarColor.rgb * ground, b.far * 0.65);         // melt into the terrain colour far away
                o.color = half4(col, b.t);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 albedo = i.color.rgb;
                Light main = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = lerp(1, main.shadowAttenuation, _ShadowStrength);
                // soft normal: the blade normal (flipped to the visible side) blended 60% towards up. Wrapped
                // lighting so a blade turned away from the sun is never black, and nothing flips per pixel.
                half3 nb = normalize(i.normalWS) * (front ? 1 : -1);
                half3 n = normalize(lerp(nb, half3(0, 1, 0), 0.6));
                half ndl = saturate(dot(n, main.direction) * 0.5 + 0.5);
                half lit = ndl * shadow;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                half occ = ao.indirectAmbientOcclusion;
                #else
                half occ = 1;
                #endif
                occ *= lerp(0.6, 1, i.color.a);                                // self-shadowed roots
                half3 ambient = SampleSH(half3(0, 1, 0)) * occ;
                half3 col = albedo * lerp(_ShadowColor.rgb * (ambient + 0.12), main.color + ambient * 0.35, lit);
                // the side of a blade facing away from the sun cools towards blue
                half away = saturate(-dot(nb.xz, normalize(main.direction.xz + 1e-4)));
                col = lerp(col, col * _ShadowColor.rgb * 1.2, away * 0.22);
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
