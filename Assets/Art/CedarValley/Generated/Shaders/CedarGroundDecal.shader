// Ground decals (trailbeds, trampled camp snow): lit like the ground, alpha from vertex colour,
// so packed paths fade into the surrounding snow without visible edges.
Shader "CLSR/Ground Decal"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Detail", 2D) = "white" {}
        _MainWorld ("Detail uses world XZ (0/1)", Range(0,1)) = 0
        _MainTile ("World detail tile (m)", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Geometry+15" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Offset -1, -2
        CGPROGRAM
        #pragma surface surf Lambert alpha:fade
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        float _MainWorld, _MainTile;
        struct Input { float2 uv_MainTex; float3 worldPos; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            float2 uv = lerp(IN.uv_MainTex, IN.worldPos.xz / _MainTile, _MainWorld);
            o.Albedo = tex2D(_MainTex, uv).rgb * IN.color.rgb * _Color.rgb;
            o.Alpha = IN.color.a * _Color.a;
        }
        ENDCG
    }
    FallBack Off
}
