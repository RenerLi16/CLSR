// Ground decals (earth-and-snow trail beds, trampled camp snow) under the winter light.
// Alpha comes from vertex colour; a world-space noise breaks up the soft edges so a path
// frays into the surrounding snow instead of ending on a clean border.
Shader "CLSR/Winter Decal"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Detail", 2D) = "white" {}
        _MainWorld ("Detail uses world XZ (0/1)", Range(0,1)) = 0
        _MainTile ("World detail tile (m)", Float) = 4
        _NoiseTex ("Edge noise", 2D) = "gray" {}
        _NoiseTile ("Edge noise tile (m)", Float) = 9
        _EdgeBreak ("Edge break-up", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Geometry+15" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Offset -1, -2
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "CedarWinter.cginc"

            sampler2D _MainTex, _NoiseTex;
            float4 _MainTex_ST;
            half4 _Color;
            float _MainWorld, _MainTile, _NoiseTile, _EdgeBreak;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; half3 wn : TEXCOORD1; half4 col : TEXCOORD2; float2 uv : TEXCOORD3; SHADOW_COORDS(4) };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.col = v.color;
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                TRANSFER_SHADOW(o);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = lerp(i.uv, i.wpos.xz / _MainTile, _MainWorld);
                half4 t = tex2D(_MainTex, uv);
                half noise = tex2D(_NoiseTex, i.wpos.xz / _NoiseTile).r * 0.65 + tex2D(_NoiseTex, i.wpos.xz / (_NoiseTile * 0.29)).r * 0.35;
                half a = saturate(i.col.a + (noise - 0.5) * _EdgeBreak * (1 - i.col.a) * 2.2) * _Color.a;
                half3 albedo = t.rgb * i.col.rgb * _Color.rgb;
                half3 lit = WinterLit(albedo, normalize(i.wn), 1, SHADOW_ATTENUATION(i), 0.05h);
                return half4(WinterFinish(lit, i.wpos), a);
            }
            ENDCG
        }
    }
    FallBack Off
}
