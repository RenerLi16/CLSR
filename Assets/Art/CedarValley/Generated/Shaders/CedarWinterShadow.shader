// Baked cast shadows for trees and props: long, soft ellipses along the low winter sun.
// The atlas holds a smooth falloff (left half) and a dappled, branch-broken falloff (right
// half) for leafless crowns. A stencil keeps overlapping shadows from stacking darker than
// one shadow, as real shadows do; faint edges are skipped so they never block a darker core.
// Shadows fade with the haze so distant slopes stay soft.
Shader "CLSR/Winter Cast Shadow"
{
    Properties
    {
        _MainTex ("Falloff atlas (alpha)", 2D) = "white" {}
        _Strength ("Strength", Range(0,1)) = 1
        _EdgeSkip ("Skip below", Range(0,0.5)) = 0.06
    }
    SubShader
    {
        Tags { "Queue"="Geometry+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp NotEqual Pass Replace }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "CedarWinter.cginc"
            sampler2D _MainTex;
            float _Strength, _EdgeSkip;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float3 wpos : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                half s = tex2D(_MainTex, i.uv).a * i.color.a * _Strength;
                clip(s - _EdgeSkip);
                s *= 1 - WinterHazeAmount(i.wpos);
                return fixed4(lerp(fixed3(1, 1, 1), i.color.rgb, s), 1);
            }
            ENDCG
        }
    }
}
