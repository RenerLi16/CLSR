// Alpha-tested crown cards for frosted deciduous twigs, conifer sprays, shrubs and dead grass.
// The atlas carries colour (frost already painted on the twigs) and coverage in alpha.
// Vertex colour tints each tree; vertex alpha adds extra hoar frost. Normals are spherical
// (pointing out of the crown), so cards shade together as one soft, volumetric canopy.
Shader "CLSR/Winter Foliage"
{
    Properties
    {
        _MainTex ("Atlas (rgb colour, a coverage)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.42
        _Color ("Tint", Color) = (1,1,1,1)
        _FrostColor ("Hoar frost", Color) = (0.93,0.95,0.97,1)
        _Wrap ("Extra light wrap", Range(0,1)) = 0.35
        _Backlight ("Backlit glow", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 200
        Cull Off
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

            sampler2D _MainTex;
            half _Cutoff, _Wrap, _Backlight;
            half4 _Color, _FrostColor;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
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
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.col = v.color;
                o.uv = v.texcoord;
                TRANSFER_SHADOW(o);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 t = tex2D(_MainTex, i.uv);
                clip(t.a - _Cutoff);
                half3 n = normalize(i.wn);
                half3 albedo = t.rgb * i.col.rgb * _Color.rgb;
                albedo = lerp(albedo, _FrostColor.rgb * (0.9 + 0.1 * t.g), saturate(i.col.a) * 0.6);
                half atten = SHADOW_ATTENUATION(i);
                half3 lit = WinterLit(albedo, n, 1, atten, _Wrap);
                half3 v = normalize(UnityWorldSpaceViewDir(i.wpos));
                half back = pow(saturate(dot(-v, _WorldSpaceLightPos0.xyz)), 3) * _Backlight;
                lit += WinterSun() * albedo * back * 0.4;
                return half4(WinterFinish(lit, i.wpos), 1);
            }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            half _Cutoff;
            struct v2f { V2F_SHADOW_CASTER; float2 uv : TEXCOORD1; };
            v2f vert (appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) o.uv = v.texcoord.xy; return o; }
            float4 frag (v2f i) : SV_Target { clip(tex2D(_MainTex, i.uv).a - _Cutoff); SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    FallBack Off
}
