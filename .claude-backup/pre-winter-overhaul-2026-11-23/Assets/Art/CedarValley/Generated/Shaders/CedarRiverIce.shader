Shader "CLSR/Cedar River Ice"
{
    Properties
    {
        _MainTex ("Ice fractures", 2D) = "gray" {}
        _MacroTex ("Frozen pools", 2D) = "gray" {}
        _SnowTex ("Bank snow", 2D) = "white" {}
        _SnowTile ("Snow tile (m)", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        CGPROGRAM
        #pragma surface surf Lambert
        #pragma target 3.0
        sampler2D _MainTex, _MacroTex, _SnowTex;
        float _SnowTile;
        struct Input { float2 uv_MainTex; float3 worldPos; float4 color : COLOR; };
        void surf(Input i, inout SurfaceOutput o)
        {
            float2 channel = i.uv_MainTex;
            half variation = tex2D(_MacroTex, channel / float2(23, 110)).r;
            half3 ice = tex2D(_MainTex, channel / float2(9, 22) + variation * .25).rgb;
            half3 snow = tex2D(_SnowTex, i.worldPos.xz / _SnowTile).rgb * half3(.90, .93, .95);
            half shelf = saturate(i.color.a + (variation - .51) * .31);
            ice *= i.color.rgb * lerp(half3(.60,.77,.82), half3(.95,1.05,1.06), variation);
            o.Albedo = lerp(ice, snow, smoothstep(.02,.97,shelf));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
