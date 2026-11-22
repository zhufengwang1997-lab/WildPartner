// WildPartner/Sky — gradient skybox (zenith -> horizon -> ground) with a soft sun disc and stars at
// night. DayNightCycle drives the colours through global properties so one material serves all times.
Shader "WildPartner/Sky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _WP_SkyZenith, _WP_SkyHorizon, _WP_SkyGround, _WP_SunColor;
            float4 _WP_SunDir;   // xyz = direction towards the sun, w = night factor (stars)
            float _WP_WindTime;

            float vhash(float2 p) { p = frac(p * float2(233.34, 851.73)); p += dot(p, p + 23.45); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(vhash(i), vhash(i + float2(1, 0)), f.x), lerp(vhash(i + float2(0, 1)), vhash(i + 1), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { s += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.dir = v.positionOS.xyz; return o; }

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                // blend in sqrt (roughly perceptual) space with a curve that reaches the zenith blue quickly: a linear
                // blend of the warm horizon and the blue zenith turned most of the sky lilac-grey (画面整改 5, round 2)
                float tz = 1 - pow(1 - saturate(up), 3.2);
                float3 skyMix = lerp(sqrt(_WP_SkyHorizon.rgb), sqrt(_WP_SkyZenith.rgb), tz);
                float3 c = up > 0 ? skyMix * skyMix
                                  : lerp(_WP_SkyHorizon.rgb, _WP_SkyGround.rgb, saturate(-up * 4));
                float sd = dot(d, normalize(_WP_SunDir.xyz));
                c += _WP_SunColor.rgb * (smoothstep(0.9993, 0.9996, sd) * 4 + pow(saturate(sd), 64) * 0.6 + pow(saturate(sd), 6) * 0.15);
                // moon opposite the sun
                c += float3(0.8, 0.85, 1) * smoothstep(0.9990, 0.9993, dot(d, -normalize(_WP_SunDir.xyz))) * _WP_SunDir.w;
                // cumulus (画面整改 5, round 2): bigger puffy clouds with a domain-warped billow noise, crisp toon edges,
                // a warm lit crown and a blue-violet underside; thinning towards the zenith, fading into the horizon haze
                float3 sunDir = normalize(_WP_SunDir.xyz);
                float2 cuv = d.xz / (max(up, 0) + 0.16) * 0.32 + float2(_WP_WindTime * 0.004, _WP_WindTime * 0.0015);
                float2 warp = float2(fbm(cuv * 0.7 + 3.1), fbm(cuv * 0.7 - 5.7)) - 0.5;
                float n = fbm(cuv + warp * 0.9);
                n = n * 0.75 + vnoise(cuv * 0.45 + 9.3) * 0.35;                         // large-scale clumping: gaps between clouds
                float band = smoothstep(0.015, 0.1, up) * (1 - smoothstep(0.55, 0.95, up) * 0.6);
                float cover = smoothstep(0.50, 0.56, n) * band;
                float nSun = fbm(cuv + warp * 0.9 + sunDir.xz * 0.09) * 0.75 + vnoise(cuv * 0.45 + 9.3 + sunDir.xz * 0.04) * 0.35;
                float thick = saturate((n - 0.5) * 3.5);
                float lightAmt = saturate((n - nSun) * 7 + 0.62 - thick * 0.45);
                lightAmt = smoothstep(0.35, 0.6, lightAmt) * 0.8 + lightAmt * 0.2;       // two soft toon bands
                float day = saturate(dot(_WP_SunColor.rgb, 0.333) * 2);
                float3 lit = lerp(float3(1.12, 1.08, 1.02), _WP_SunColor.rgb * 1.25, 0.28) * lerp(0.35, 1.0, day);
                float3 shade = lerp(_WP_SkyZenith.rgb, float3(0.56, 0.55, 0.78), 0.5) * lerp(0.55, 0.86, day);
                float3 cloud = lerp(shade, lit, lightAmt);
                cloud += _WP_SunColor.rgb * pow(saturate(sd), 10) * smoothstep(0.5, 0.53, n) * (1 - smoothstep(0.53, 0.6, n)) * 0.8;   // silver lining
                float horizonFade = smoothstep(0.0, 0.12, up);
                c = lerp(c, lerp(_WP_SkyHorizon.rgb, cloud, horizonFade * 0.85 + 0.15), cover * 0.96);

                float3 cell = floor(d * 280);
                float s = step(0.9975, hash(cell)) * saturate(up * 3) * _WP_SunDir.w * (1 - cover);
                c += s;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
