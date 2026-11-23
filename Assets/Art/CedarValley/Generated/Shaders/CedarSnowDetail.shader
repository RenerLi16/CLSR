// Props, trees, ice and trailbeds: detail texture x vertex colour, blended toward world-space
// snow by vertex alpha (and optionally by upward-facing normals) so edges melt into the ground.
Shader "CLSR/Snow Detail"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Detail", 2D) = "white" {}
        _MainWorld ("Detail uses world XZ (0/1)", Range(0,1)) = 0
        _MainTile ("World detail tile (m)", Float) = 10
        _SnowTex ("Snow", 2D) = "white" {}
        _SnowTile ("Snow tile (m)", Float) = 8
        _SnowColor ("Snow tint", Color) = (1,1,1,1)
        _AutoSnow ("Snow on upward faces", Range(0,1)) = 0
        _AutoSnowStart ("Upward snow start", Range(0,1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _SnowTex;
        fixed4 _Color, _SnowColor;
        float _MainWorld, _MainTile, _SnowTile, _AutoSnow, _AutoSnowStart;
        struct Input { float2 uv_MainTex; float3 worldPos; float3 worldNormal; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            float2 uv = lerp(IN.uv_MainTex, IN.worldPos.xz / _MainTile, _MainWorld);
            fixed3 d = tex2D(_MainTex, uv).rgb * IN.color.rgb * _Color.rgb;
            fixed3 s = tex2D(_SnowTex, IN.worldPos.xz / _SnowTile).rgb * _SnowColor.rgb;
            float up = saturate((normalize(IN.worldNormal).y - _AutoSnowStart) * 5) * _AutoSnow;
            o.Albedo = lerp(d, s, saturate(IN.color.a + up));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
