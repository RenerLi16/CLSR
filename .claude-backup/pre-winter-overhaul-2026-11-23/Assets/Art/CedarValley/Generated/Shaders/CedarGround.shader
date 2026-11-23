// Cedar Valley ground: world-space snow, packed snow and triplanar rock, blended by vertex colour.
// R = packed snow, G = exposed rock, B = forest-floor shade, A = baked ambient occlusion.
Shader "CLSR/Cedar Ground"
{
    Properties
    {
        _SnowTex ("Fresh snow", 2D) = "white" {}
        _SnowTile ("Fresh snow tile (m)", Float) = 8
        _SnowColor ("Snow tint", Color) = (1,1,1,1)
        _PackedTex ("Packed snow", 2D) = "white" {}
        _PackedTile ("Packed snow tile (m)", Float) = 4
        _RockTex ("Rock", 2D) = "gray" {}
        _RockTile ("Rock tile (m)", Float) = 6
        _MacroTex ("Macro variation", 2D) = "gray" {}
        _MacroTile ("Macro tile (m)", Float) = 180
        _MacroStrength ("Macro strength", Range(0,0.3)) = 0.07
        _ForestTint ("Forest floor tint", Color) = (0.74,0.79,0.84,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0
        sampler2D _SnowTex, _PackedTex, _RockTex, _MacroTex;
        float _SnowTile, _PackedTile, _RockTile, _MacroTile, _MacroStrength;
        fixed4 _ForestTint, _SnowColor;
        struct Input { float3 worldPos; float3 worldNormal; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            float3 wp = IN.worldPos;
            float3 n = normalize(IN.worldNormal);
            half macro = tex2D(_MacroTex, wp.xz / _MacroTile).r;
            half3 snow = tex2D(_SnowTex, wp.xz / _SnowTile).rgb * _SnowColor.rgb;
            half3 packed = tex2D(_PackedTex, wp.xz / _PackedTile).rgb * half3(.90, .87, .82);
            float3 bw = pow(abs(n), 4);
            bw /= (bw.x + bw.y + bw.z);
            // Keep strata angled across both vertical projections, at the same world height.
            float strata = wp.y + wp.x * .18 - wp.z * .24 + macro * 2.8;
            half3 rock = tex2D(_RockTex, float2(wp.z, strata * .61) / _RockTile).rgb * bw.x
                       + tex2D(_RockTex, wp.xz / _RockTile).rgb * bw.y
                       + tex2D(_RockTex, float2(wp.x, strata * .61) / _RockTile).rgb * bw.z;
            half fracture = smoothstep(.64, .92, tex2D(_MacroTex, float2((wp.x + wp.z) / 39, strata / 11)).r);
            half exposure = saturate(IN.color.g + IN.color.g * (1 - IN.color.g) * (fracture * 1.1 - .24));
            half3 c = lerp(snow, packed, IN.color.r);
            c = lerp(c, c * _ForestTint.rgb, IN.color.b * .88);
            c = lerp(c, rock * half3(1.06, .99, .91) * (1 - .16 * fracture), exposure);
            c *= 1 + (macro - .52) * 2 * _MacroStrength;
            c *= IN.color.a;
            o.Albedo = c;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
