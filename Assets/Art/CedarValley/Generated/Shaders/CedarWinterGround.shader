// Cedar Valley winter ground. Vertex colour: R packed snow, G exposed rock, B woodland shade,
// A ambient occlusion. UV0: x thin snow (soil and dead grass show through), y wind scour.
// Six snow conditions blend from these masks: fresh, packed (paths), wind-scoured (crests),
// woodland-shaded, thin over soil, and snow lodged on rock ledges. All detail is world-space
// (triplanar on steep faces) so nothing stretches on slopes.
Shader "CLSR/Winter Ground"
{
    Properties
    {
        _SnowTex ("Fresh snow", 2D) = "white" {}
        _SnowNrm ("Snow slope map (rg)", 2D) = "gray" {}
        _SnowTile ("Snow tile (m)", Float) = 8
        _SnowColor ("Snow tint", Color) = (0.95,0.955,0.96,1)
        _ShadeSnow ("Woodland snow tint", Color) = (0.83,0.85,0.88,1)
        _ScourColor ("Wind-scoured snow tint", Color) = (0.9,0.91,0.93,1)
        _PackedTex ("Packed snow", 2D) = "white" {}
        _PackedTile ("Packed snow tile (m)", Float) = 4
        _SoilTex ("Frozen soil and grass", 2D) = "gray" {}
        _SoilTile ("Soil tile (m)", Float) = 5
        _RockTex ("Rock", 2D) = "gray" {}
        _RockNrm ("Rock slope map (rg)", 2D) = "gray" {}
        _RockTile ("Rock tile (m)", Float) = 7
        _RockTint ("Rock tint", Color) = (0.96,0.96,0.97,1)
        _MacroTex ("Macro variation", 2D) = "gray" {}
        _MacroTile ("Macro tile (m)", Float) = 180
        _MacroStrength ("Macro strength", Range(0,0.3)) = 0.08
        _NormalStrength ("Snow relief", Range(0,1.5)) = 0.4
        _AOStrength ("Occlusion strength", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "CedarWinter.cginc"

            sampler2D _SnowTex, _SnowNrm, _PackedTex, _SoilTex, _RockTex, _RockNrm, _MacroTex;
            float _SnowTile, _PackedTile, _SoilTile, _RockTile, _MacroTile, _MacroStrength, _NormalStrength, _AOStrength;
            half4 _SnowColor, _ShadeSnow, _ScourColor, _RockTint;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                half3 wn : TEXCOORD1;
                half4 col : TEXCOORD2;
                half2 extra : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.col = v.color;
                o.extra = v.texcoord;
                TRANSFER_SHADOW(o);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 wp = i.wpos;
                half3 n0 = normalize(i.wn);
                half macro = tex2D(_MacroTex, wp.xz / _MacroTile).r;
                half macro2 = tex2D(_MacroTex, wp.xz / (_MacroTile * 0.23) + 0.37).r;
                half packedM = i.col.r, rockM = i.col.g, shadeM = i.col.b, ao = i.col.a;
                half thin = i.extra.x, scour = i.extra.y;

                // ---- snow (fresh, scoured, woodland)
                float2 suv = wp.xz / _SnowTile;
                half3 snow = tex2D(_SnowTex, suv).rgb * _SnowColor.rgb;
                half2 sn = tex2D(_SnowNrm, suv).rg * 2 - 1;
                // wind ripples: the same relief stretched across the prevailing wind on exposed ground
                float2 wuv = float2(wp.x * 0.83 + wp.z * 0.55, wp.z * 0.83 - wp.x * 0.55) / _SnowTile * float2(0.45, 2.2);
                half2 ripple = tex2D(_SnowNrm, wuv).rg * 2 - 1;
                sn = lerp(sn, ripple * 1.6, saturate(scour * 1.4));
                half3 c = lerp(snow, snow * _ScourColor.rgb, scour);
                c = lerp(c, snow * _ShadeSnow.rgb, shadeM * 0.85);

                // ---- thin snow: frozen soil and dead grass through patchy cover
                half4 soil = tex2D(_SoilTex, wp.xz / _SoilTile);
                half patch = saturate((thin * (0.55 + 0.9 * macro2) + soil.a * 0.45 - 0.62) * 3.2) * (1 - saturate(rockM * 1.5));
                c = lerp(c, soil.rgb, patch);

                // ---- packed snow near trails and camp (slightly warm, flattened)
                half3 packed = tex2D(_PackedTex, wp.xz / _PackedTile).rgb * half3(0.93, 0.91, 0.87);
                c = lerp(c, packed, packedM);
                sn *= 1 - packedM * 0.6;

                // ---- exposed rock (triplanar) with snow caught on ledges and in cracks
                float3 bw = pow(abs(n0), 4);
                bw /= (bw.x + bw.y + bw.z);
                float strata = wp.y + wp.x * 0.16 - wp.z * 0.21 + macro * 3.0;
                float2 ux = float2(wp.z, strata * 0.62) / _RockTile, uy = wp.xz / _RockTile, uz = float2(wp.x, strata * 0.62) / _RockTile;
                half3 rock = tex2D(_RockTex, ux).rgb * bw.x + tex2D(_RockTex, uy).rgb * bw.y + tex2D(_RockTex, uz).rgb * bw.z;
                half2 rnx = tex2D(_RockNrm, ux).rg * 2 - 1, rny = tex2D(_RockNrm, uy).rg * 2 - 1, rnz = tex2D(_RockNrm, uz).rg * 2 - 1;
                half3 rockN = normalize(n0 + (half3(0, rnx.y, rnx.x) * bw.x + half3(rny.x, 0, rny.y) * bw.y + half3(rnz.x, rnz.y, 0) * bw.z) * 0.35);
                rock *= _RockTint.rgb * (0.92 + 0.16 * macro2);
                half rockLum = dot(rock, half3(0.33, 0.34, 0.33));
                // snow settles where the fractured face turns up and in the darker cracks
                half ledge = saturate((rockN.y - 0.42) * 2.6) + saturate((0.36 - rockLum) * 4.0) * 0.45;
                half exposure = saturate(rockM * (1.15 - ledge * 0.8 * (1 - scour * 0.5)));
                exposure = smoothstep(0.08, 0.92, exposure);
                // rock only shows through on real slopes; gentle ground keeps its snow
                exposure *= smoothstep(0.985, 0.86, n0.y);
                c = lerp(c, rock, exposure);

                // ---- normals and macro tone
                half3 n = WinterPerturbUp(n0, sn, _NormalStrength * (1 - exposure));
                n = normalize(lerp(n, rockN, exposure));
                c *= 1 + (macro - 0.52) * 2 * _MacroStrength;

                half atten = SHADOW_ATTENUATION(i);
                half occ = lerp(1, ao, _AOStrength);
                half3 lit = WinterLit(c, n, occ, atten, 0.05h);
                return half4(WinterFinish(lit, wp), 1);
            }
            ENDCG
        }
    }
    FallBack "Legacy Shaders/VertexLit"
}
