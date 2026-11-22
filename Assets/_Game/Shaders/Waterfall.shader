// WildPartner/Waterfall — the falling water sheet (试玩反馈 1123c-A4).
// Not depth-faded like the river (that made the fall vanish up close): a fixed-opacity sheet, both sides drawn,
// with streaks scrolling down the fall (uv.y = metres along the sheet from the lip), white water at the lip and a
// foam skirt at the bottom (uv.y near _Length). Mode "Foam": the round foam patch on the plunge pool (uv = disc
// coords -1..1), with drifting foam and slow expanding rings.
// Transparent, no motion vectors: renderers carry WaterfallSurface so the upscaler can write them to the reactive mask.
Shader "WildPartner/Waterfall"
{
    Properties
    {
        _Color ("Water", Color) = (0.55, 0.78, 0.86, 0.78)
        _DeepColor ("Water (inner)", Color) = (0.22, 0.48, 0.62, 0.85)
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _Speed ("Fall Speed (m/s)", Float) = 4.5
        _Length ("Sheet Length (m)", Float) = 12
        _Foam ("Foam Mode (0 sheet, 1 pool disc)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ForwardFall"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _DeepColor, _FoamColor;
                float _Speed, _Length, _Foam;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; half fog : TEXCOORD2; float4 color : COLOR; };

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + 1), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                Light main = GetMainLight();
                half3 light = main.color * 0.75 + SampleSH(float3(0, 1, 0)) * 0.8;
                half4 col;
                if (_Foam > 0.5)
                {
                    // plunge pool: dense foam in the middle thinning out, drifting noise, rings travelling outwards
                    float r = length(i.uv);
                    float2 p = i.uv * 6;
                    float n = vnoise(p + float2(t * 0.6, -t * 0.4)) * vnoise(p * 1.9 - t * 0.5);
                    float rings = smoothstep(0.75, 0.95, sin((r * 9 - t * 2.2) * 3.14159) * 0.5 + 0.5) * (1 - r);
                    float dense = 1 - smoothstep(0.15, 0.75, r);
                    float foam = saturate(dense * (0.55 + n * 1.2) + rings * 0.6 + smoothstep(0.35, 0.6, n) * (1 - r) * 0.6);
                    col = half4(_FoamColor.rgb * light, foam * (1 - smoothstep(0.8, 1, r)));
                }
                else
                {
                    // sheet: x across (0..1), y metres from the lip. Streaks stretched along the fall, scrolling down.
                    float y = i.uv.y, x = i.uv.x;
                    float2 s = float2(x * 14, y * 0.35 - t * _Speed * 0.35);
                    float streak = vnoise(s) * 0.6 + vnoise(s * float2(2.3, 1.7) + 4.1) * 0.4;
                    float fine = vnoise(float2(x * 40, y * 1.2 - t * _Speed * 1.2));
                    float edge = smoothstep(0, 0.12, x) * smoothstep(0, 0.12, 1 - x);   // ragged side edges
                    col = lerp(_Color, _DeepColor, streak * 0.8);
                    float white = smoothstep(0.55, 0.85, streak + fine * 0.3);
                    float lip = 1 - smoothstep(0.0, 1.6, y);                           // white water over the edge
                    float skirt = smoothstep(_Length - 2.5, _Length, y);               // foam where it lands
                    float foam = saturate(white * 0.75 + lip * (0.5 + fine * 0.5) + skirt * (0.6 + fine * 0.6));
                    col.rgb = lerp(col.rgb, _FoamColor.rgb, foam);
                    col.rgb *= light;
                    col.a = saturate(lerp(_Color.a, 1, foam) * (edge * (0.75 + 0.25 * vnoise(float2(x * 9, y * 0.8 - t * _Speed * 0.5)))) );
                }
                col.a *= i.color.a;
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
    }
}
