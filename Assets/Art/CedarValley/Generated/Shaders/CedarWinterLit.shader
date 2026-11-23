// Props, timber, rocks, bark and solid conifer boughs under the shared winter light.
// Detail texture x tint (x vertex colour when _UseVertexColor = 1); snow from vertex alpha
// and, optionally, on upward-facing surfaces (roofs, ledges, the tops of logs and boughs).
Shader "CLSR/Winter Lit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Detail", 2D) = "white" {}
        _MainWorld ("Detail uses world XZ (0/1)", Range(0,1)) = 0
        _MainTile ("World detail tile (m)", Float) = 10
        _UseVertexColor ("Use vertex colour (0/1)", Range(0,1)) = 1
        _SnowTex ("Snow", 2D) = "white" {}
        _SnowTile ("Snow tile (m)", Float) = 8
        _SnowColor ("Snow tint", Color) = (0.94,0.95,0.965,1)
        _AutoSnow ("Snow on upward faces", Range(0,1)) = 0
        _AutoSnowStart ("Upward snow start", Range(0,1)) = 0.7
        _Wrap ("Extra light wrap", Range(0,1)) = 0.1
        _Backlight ("Backlit frost glow", Range(0,1)) = 0
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
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "CedarWinter.cginc"

            sampler2D _MainTex, _SnowTex;
            float4 _MainTex_ST;
            half4 _Color, _SnowColor;
            float _MainWorld, _MainTile, _UseVertexColor, _SnowTile, _AutoSnow, _AutoSnowStart, _Wrap, _Backlight;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 texcoord : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                half3 wn : TEXCOORD1;
                half4 col : TEXCOORD2;
                float2 uv : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.col = lerp(half4(1, 1, 1, 0), v.color, _UseVertexColor);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                TRANSFER_SHADOW(o);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half3 n = normalize(i.wn);
                float2 uv = lerp(i.uv, i.wpos.xz / _MainTile, _MainWorld);
                half3 d = tex2D(_MainTex, uv).rgb * i.col.rgb * _Color.rgb;
                half3 s = tex2D(_SnowTex, i.wpos.xz / _SnowTile).rgb * _SnowColor.rgb;
                half up = saturate((n.y - _AutoSnowStart) * 5) * _AutoSnow;
                half snow = saturate(i.col.a + up);
                half3 albedo = lerp(d, s, snow);
                half atten = SHADOW_ATTENUATION(i);
                half3 lit = WinterLit(albedo, n, 1, atten, _Wrap);
                // frost and snow edges glow faintly when the low sun is behind them
                half3 v = normalize(UnityWorldSpaceViewDir(i.wpos));
                half back = pow(saturate(dot(-v, _WorldSpaceLightPos0.xyz)), 3) * _Backlight * (0.35 + 0.65 * snow);
                lit += WinterSun() * albedo * back * 0.35;
                return half4(WinterFinish(lit, i.wpos), 1);
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert (appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) return o; }
            float4 frag (v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    FallBack "Legacy Shaders/VertexLit"
}
