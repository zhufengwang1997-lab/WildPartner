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
                float3 c = up > 0 ? lerp(_WP_SkyHorizon.rgb, _WP_SkyZenith.rgb, pow(saturate(up), 0.35))
                                  : lerp(_WP_SkyHorizon.rgb, _WP_SkyGround.rgb, saturate(-up * 4));
                float sd = dot(d, normalize(_WP_SunDir.xyz));
                c += _WP_SunColor.rgb * (smoothstep(0.9993, 0.9996, sd) * 4 + pow(saturate(sd), 64) * 0.6 + pow(saturate(sd), 6) * 0.15);
                // moon opposite the sun
                c += float3(0.8, 0.85, 1) * smoothstep(0.9990, 0.9993, dot(d, -normalize(_WP_SunDir.xyz))) * _WP_SunDir.w;
                // soft cumulus (画面整改 5): noise on a flat cloud layer, lit side warm white, underside blue-grey,
                // thinning towards the zenith and fading into the horizon haze
                float3 sunDir = normalize(_WP_SunDir.xyz);
                float2 cuv = d.xz / (max(up, 0) + 0.14) * 0.55 + float2(_WP_WindTime * 0.006, _WP_WindTime * 0.002);
                float n = fbm(cuv);
                float band = smoothstep(0.01, 0.12, up) * (1 - smoothstep(0.6, 0.98, up) * 0.5);
                float cover = smoothstep(0.42, 0.6, n) * band;
                float nSun = fbm(cuv + sunDir.xz * 0.12);
                float lightAmt = saturate((n - nSun) * 5 + 0.5) * smoothstep(0.42, 0.75, n * 0.6 + 0.3);
                float day = saturate(dot(_WP_SunColor.rgb, 0.333) * 2);
                float3 lit = lerp(float3(1.08, 1.04, 1.0), _WP_SunColor.rgb * 1.2, 0.3) * lerp(0.35, 1.0, day);
                float3 shade = lerp(_WP_SkyZenith.rgb, float3(0.58, 0.57, 0.76), 0.55) * lerp(0.62, 0.8, day);
                float3 cloud = lerp(shade, lit, lightAmt);
                cloud += _WP_SunColor.rgb * pow(saturate(sd), 12) * (1 - cover) * 0.6;   // silver lining towards the sun
                c = lerp(c, cloud, cover * 0.92);

                float3 cell = floor(d * 280);
                float s = step(0.9975, hash(cell)) * saturate(up * 3) * _WP_SunDir.w * (1 - cover);
                c += s;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
