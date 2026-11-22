#ifndef WP_TOON_COMMON_INCLUDED
#define WP_TOON_COMMON_INCLUDED

// Shared by every pass of WildPartner/Toon. All per-material values live in UnityPerMaterial so the
// SRP Batcher and the GPU Resident Drawer (BatchRendererGroup) can batch them (FR-RD-04).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half4  _ShadowColor;
    half4  _RimColor;
    half   _RimPower;
    half   _RimAmbient;
    half   _Cutoff;
    half   _RampSteps;
    half   _RampSmooth;
    half   _ShadowStrength;
    half   _SphereNormal;
    half   _WindStrength;
    half   _WindFrequency;
    half   _Translucency;
    half   _Emission;
    half   _UpNormal;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
#endif

// Global wind, set by WindController (direction xz, strength multiplier, gust phase).
float4 _WP_Wind;          // xy = direction (xz plane), z = global strength, w = unused
float  _WP_WindTime;      // time used this frame
float  _WP_WindTimePrev;  // time used last frame (for motion vectors, IN-03)

// Vertex colour R = sway weight painted by the asset generator (0 at the trunk base, 1 at leaf tips).
// Object position feeds a per-instance phase so neighbouring trees do not move in lock-step.
float3 WindOffsetWS(float3 positionWS, float3 pivotWS, half weight, float t)
{
#if defined(_WIND)
    float2 dir = _WP_Wind.xy;
    float phase = dot(pivotWS.xz, float2(0.11, 0.07));
    float main = sin(t * _WindFrequency + phase) * 0.6 + sin(t * _WindFrequency * 2.3 + phase * 1.7) * 0.25;
    float flutter = sin(t * _WindFrequency * 5.1 + dot(positionWS, float3(1.7, 1.3, 2.1))) * 0.15;
    float amount = (main + flutter + 0.35) * _WindStrength * _WP_Wind.z * weight;
    return float3(dir.x, 0, dir.y) * amount + float3(0, -abs(amount) * 0.15, 0);
#else
    return 0;
#endif
}

float3 ApplyWindOS(float3 positionOS, half weight, float t)
{
#if defined(_WIND)
    float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));
    float3 positionWS = TransformObjectToWorld(positionOS);
    positionWS += WindOffsetWS(positionWS, pivotWS, weight, t);
    return TransformWorldToObject(positionWS);
#else
    return positionOS;
#endif
}

// Same as above but with last frame's object matrix (used by the motion-vector pass).
float3 ApplyWindOSPrev(float3 positionOS, half weight, float t)
{
#if defined(_WIND)
    float3 pivotWS = mul(UNITY_PREV_MATRIX_M, float4(0, 0, 0, 1)).xyz;
    float3 positionWS = mul(UNITY_PREV_MATRIX_M, float4(positionOS, 1)).xyz;
    positionWS += WindOffsetWS(positionWS, pivotWS, weight, t);
    return mul(UNITY_PREV_MATRIX_I_M, float4(positionWS, 1)).xyz;
#else
    return positionOS;
#endif
}

#endif
