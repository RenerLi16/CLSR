// Soft baked contact shadows: multiplies the ground under trees and props.
// Real-time shadows don't reach this distant orthographic camera, so these ground the scenery.
Shader "CLSR/Contact Shadow"
{
    Properties
    {
        _MainTex ("Falloff (alpha)", 2D) = "white" {}
        _Strength ("Strength", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Geometry+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Strength;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed s = tex2D(_MainTex, i.uv).a * i.color.a * _Strength;
                return fixed4(lerp(fixed3(1,1,1), i.color.rgb, s), 1);
            }
            ENDCG
        }
    }
}
