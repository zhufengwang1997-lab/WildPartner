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

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.dir = v.positionOS.xyz; return o; }

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float3 c = up > 0 ? lerp(_WP_SkyHorizon.rgb, _WP_SkyZenith.rgb, pow(saturate(up), 0.45))
                                  : lerp(_WP_SkyHorizon.rgb, _WP_SkyGround.rgb, saturate(-up * 4));
                float sd = dot(d, normalize(_WP_SunDir.xyz));
                c += _WP_SunColor.rgb * (smoothstep(0.9993, 0.9996, sd) * 4 + pow(saturate(sd), 64) * 0.6 + pow(saturate(sd), 6) * 0.15);
                // moon opposite the sun
                c += float3(0.8, 0.85, 1) * smoothstep(0.9990, 0.9993, dot(d, -normalize(_WP_SunDir.xyz))) * _WP_SunDir.w;
                float3 cell = floor(d * 280);
                float s = step(0.9975, hash(cell)) * saturate(up * 3) * _WP_SunDir.w;
                c += s;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
