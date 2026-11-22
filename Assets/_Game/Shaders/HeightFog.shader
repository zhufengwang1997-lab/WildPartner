// WildPartner/HeightFog — full-screen aerial perspective (画面整改 4), run by a FullScreenPassRendererFeature
// before post-processing. Replaces Unity's linear fog, which only knew distance and washed the whole picture
// out to grey-white. Here density falls off exponentially with height (analytic integral along the view ray),
// so the valley floor and far hills sink into a soft blue haze while the near ground stays clear, and the
// haze turns warm when looking towards the sun. Sky pixels only receive it near the horizon.
Shader "WildPartner/HeightFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "HeightFog"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _WP_FogColor;      // rgb: haze colour away from the sun
            float4 _WP_FogSunColor;   // rgb: in-scatter colour towards the sun, a: strength
            float4 _WP_FogParams;     // x: density at base height (1/m), y: height falloff (1/m), z: base height (m), w: max opacity
            float4 _WP_FogParams2;    // x: start distance (m), y: sky horizon amount
            float4 _WP_SunDir;        // xyz: direction to the sun

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_WP_FogParams.w <= 0) return col;
                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                bool sky = raw <= 1e-6;
                #else
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                bool sky = raw >= 1 - 1e-6;
                #endif
                float3 ws = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 ray = ws - _WorldSpaceCameraPos;
                float dist = sky ? 1500 : length(ray);
                float3 dir = normalize(ray);

                float a = _WP_FogParams.x, b = max(_WP_FogParams.y, 1e-4);
                float start = min(_WP_FogParams2.x, dist);
                float hStart = _WorldSpaceCameraPos.y + dir.y * start - _WP_FogParams.z;
                float d = dist - start;
                float k = b * dir.y * d;
                // integral of a*exp(-b*h) along the ray; the series form avoids 0/0 on level rays
                float amount = a * exp(-b * hStart) * d * (abs(k) > 1e-3 ? (1 - exp(-k)) / k : 1 - 0.5 * k);
                float f = saturate(1 - exp(-amount)) * _WP_FogParams.w;
                if (sky) f *= saturate(1 - dir.y * 7) * _WP_FogParams2.y;

                float toSun = pow(saturate(dot(dir, _WP_SunDir.xyz)), 5);
                half3 fogCol = lerp(_WP_FogColor.rgb, _WP_FogSunColor.rgb, toSun * _WP_FogSunColor.a);
                col.rgb = lerp(col.rgb, fogCol, f);
                return col;
            }
            ENDHLSL
        }
    }
}
