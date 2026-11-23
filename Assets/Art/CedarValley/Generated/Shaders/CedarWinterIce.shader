// Frozen Cedar River: cold, restrained blue-grey ice with wind-polished pale sheets, darker
// clear-ice pools, and snow-drifted shelf ice that meets the bank snow without a seam.
Shader "CLSR/Winter River Ice"
{
    Properties
    {
        _MainTex ("Ice fractures", 2D) = "gray" {}
        _MacroTex ("Frozen pools", 2D) = "gray" {}
        _SnowTex ("Bank snow", 2D) = "white" {}
        _SnowNrm ("Snow slope map (rg)", 2D) = "gray" {}
        _SnowTile ("Snow tile (m)", Float) = 8
        _IceDeep ("Clear ice", Color) = (0.50,0.58,0.63,1)
        _IcePale ("Polished ice", Color) = (0.80,0.84,0.87,1)
        _Gloss ("Sun glint", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
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

            sampler2D _MainTex, _MacroTex, _SnowTex, _SnowNrm;
            float _SnowTile, _Gloss;
            half4 _IceDeep, _IcePale;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; half4 col : TEXCOORD1; float2 uv : TEXCOORD2; SHADOW_COORDS(3) };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.col = v.color;
                o.uv = v.texcoord;
                TRANSFER_SHADOW(o);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 channel = i.uv;
                half variation = tex2D(_MacroTex, channel / float2(23, 110)).r;
                half variation2 = tex2D(_MacroTex, i.wpos.xz / 37 + 0.21).r;
                half3 crack = tex2D(_MainTex, channel / float2(9, 22) + variation * 0.25).rgb;
                half fl = dot(crack, half3(0.3, 0.4, 0.3));
                half pale = saturate((variation2 - 0.38) * 2.4 + (fl - 0.6) * 1.5);
                half3 ice = lerp(_IceDeep.rgb, _IcePale.rgb, pale) * (0.86 + 0.28 * fl) * i.col.rgb;
                float2 suv = i.wpos.xz / _SnowTile;
                half3 snow = tex2D(_SnowTex, suv).rgb * half3(0.93, 0.94, 0.955);
                half shelf = smoothstep(0.02, 0.97, saturate(i.col.a + (variation - 0.51) * 0.31 + (variation2 - 0.5) * 0.22));
                half3 albedo = lerp(ice, snow, shelf);
                half3 n = half3(0, 1, 0);
                half2 sn = tex2D(_SnowNrm, suv).rg * 2 - 1;
                n = WinterPerturbUp(n, sn, 0.45 * shelf);
                half atten = SHADOW_ATTENUATION(i);
                half3 lit = WinterLit(albedo, n, 1, atten, 0);
                // a soft sheen on bare ice toward the low sun
                half3 v = normalize(UnityWorldSpaceViewDir(i.wpos));
                half3 h = normalize(v + _WorldSpaceLightPos0.xyz);
                lit += WinterSun() * pow(saturate(dot(n, h)), 24) * _Gloss * (1 - shelf) * atten;
                return half4(WinterFinish(lit, i.wpos), 1);
            }
            ENDCG
        }
    }
    FallBack "Legacy Shaders/VertexLit"
}
