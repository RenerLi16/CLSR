// Shared winter lighting for Cedar Valley (built-in pipeline, gamma colour space, WebGL-safe).
// One lighting model for ground, props, trees and ice keeps every surface in the same world:
//  * wrapped sun term (soft terminator, low winter sun) tinted warm by the light colour;
//  * hemispheric sky/bounce ambient from the scene's trilight SH (cool blue-grey in shade);
//  * a soft highlight shoulder so bright snow never clips to flat white;
//  * atmospheric perspective outside the playable board and, at close zoom, with view depth.
// Globals are pushed by WinterAtmosphere; with no component every term falls back to neutral.
#ifndef CEDAR_WINTER_INCLUDED
#define CEDAR_WINTER_INCLUDED

half4 _WinterHazeColor;   // rgb haze, a = unused
float4 _WinterBoard;      // playable board rect in world XZ: x0, z0, x1, z1
float4 _WinterHaze;       // x outside density (1/m), y depth density (1/m), z max haze, w depth start (m)
float4 _WinterLight;      // x wrap, y ambient scale, z sun scale, w shoulder knee
float _WinterRefDepth;    // eye depth of the view's focus point

inline half WinterWrap(half ndl, half extra)
{
    half w = _WinterLight.x + extra;
    return saturate((ndl + w) / (1.0h + w));
}

inline half3 WinterAmbient(half3 n)
{
    half s = _WinterLight.y > 0 ? _WinterLight.y : 1.0h;
    return ShadeSH9(half4(n, 1.0h)) * s;
}

inline half3 WinterSun()
{
    half s = _WinterLight.z > 0 ? _WinterLight.z : 1.0h;
    return _LightColor0.rgb * s;
}

// albedo lit by sun (with shadow attenuation) and sky; ao only darkens the ambient term.
inline half3 WinterLit(half3 albedo, half3 n, half ao, half atten, half extraWrap)
{
    half ndl = dot(n, _WorldSpaceLightPos0.xyz);
    return albedo * (WinterAmbient(n) * ao + WinterSun() * WinterWrap(ndl, extraWrap) * atten);
}

inline float WinterHazeAmount(float3 wpos)
{
    float2 d = max(_WinterBoard.xy - wpos.xz, wpos.xz - _WinterBoard.zw);
    float outside = length(max(d, 0.0));
    float hOut = 1.0 - exp(-outside * _WinterHaze.x);
    float depth = -mul(UNITY_MATRIX_V, float4(wpos, 1.0)).z;
    float hDepth = saturate((depth - _WinterRefDepth - _WinterHaze.w) * _WinterHaze.y);
    return saturate(1.0 - (1.0 - hOut) * (1.0 - hDepth)) * _WinterHaze.z;
}

inline half3 WinterTone(half3 c)
{
    half k = _WinterLight.w > 0 ? _WinterLight.w : 0.8h;
    half3 x = max(c - k, 0.0h);
    return min(c, k) + x / (1.0h + x / (1.0001h - k));
}

inline half3 WinterFinish(half3 c, float3 wpos)
{
    return lerp(WinterTone(c), _WinterHazeColor.rgb, WinterHazeAmount(wpos));
}

// Detail normal from a world-XZ normal map (rg = slope in x and z, 0.5 = flat), applied
// mostly where the surface faces up so steep faces keep their own triplanar detail.
inline half3 WinterPerturbUp(half3 n, half2 slope, half strength)
{
    return normalize(n + half3(slope.x, 0, slope.y) * strength * saturate(n.y * 1.4h - 0.2h));
}

#endif
