// Cedar Valley telescope: depth of field from the camera depth texture (built-in pipeline,
// WebGL 2 safe: 8-bit targets only, constant-size loops). Blur follows the thin-lens circle of
// confusion along the view axis, c = A * (1/s - 1/d) * f, so surfaces at the same view depth
// blur the same everywhere on screen. Gather-as-scatter disc bokeh at half resolution with
// separate near (foreground bleeds over focus) and far fields, then a full-resolution combine.
Shader "Hidden/CLSR/Telescope Depth Of Field"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _DofTex;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4 _TelescopeLens; // x focus distance (m), y aperture (m), z focal length (full-res px), w max blur (full-res px)

    static const int KernelCount = 43;
    static const float2 Kernel[KernelCount] = {
    float2(0.00000, 0.00000),
    float2(0.31845, 0.09851),
    float2(0.12153, 0.31039),
    float2(-0.16690, 0.28854),
    float2(-0.32965, 0.04942),
    float2(-0.24417, -0.22692),
    float2(0.02518, -0.33238),
    float2(0.27556, -0.18755),
    float2(0.66667, 0.00000),
    float2(0.60065, 0.28926),
    float2(0.41566, 0.52122),
    float2(0.14835, 0.64995),
    float2(-0.14835, 0.64995),
    float2(-0.41566, 0.52122),
    float2(-0.60065, 0.28926),
    float2(-0.66667, 0.00000),
    float2(-0.60065, -0.28926),
    float2(-0.41566, -0.52122),
    float2(-0.14835, -0.64995),
    float2(0.14835, -0.64995),
    float2(0.41566, -0.52122),
    float2(0.60065, -0.28926),
    float2(0.95534, 0.29552),
    float2(0.82579, 0.56398),
    float2(0.62286, 0.78233),
    float2(0.36460, 0.93117),
    float2(0.07393, 0.99726),
    float2(-0.22330, 0.97475),
    float2(-0.50069, 0.86562),
    float2(-0.73360, 0.67959),
    float2(-0.90132, 0.43316),
    float2(-0.98895, 0.14825),
    float2(-0.98871, -0.14983),
    float2(-0.90062, -0.43461),
    float2(-0.73251, -0.68076),
    float2(-0.49931, -0.86643),
    float2(-0.22174, -0.97511),
    float2(0.07553, -0.99714),
    float2(0.36609, -0.93058),
    float2(0.62412, -0.78133),
    float2(0.82669, -0.56266),
    float2(0.95581, -0.29399),
    float2(1.00000, 0.00080)
    };

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    // Signed blur diameter in full-resolution pixels (negative in front of the focal plane).
    float CoC(float2 uv)
    {
        float d = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
        float c = _TelescopeLens.y * (1.0 / _TelescopeLens.x - 1.0 / max(d, 1e-3)) * _TelescopeLens.z;
        return clamp(c, -_TelescopeLens.w, _TelescopeLens.w);
    }
    float Encode(float coc) { return saturate(coc / _TelescopeLens.w * 0.5 + 0.5); }
    // Decodes to a blur radius in half-resolution pixels (diameter / 2 / 2).
    float DecodeHalfRadius(float a) { return (a * 2.0 - 1.0) * _TelescopeLens.w * 0.25; }
    float Max3(float3 c) { return max(c.r, max(c.g, c.b)); }

    // 0: full res -> half res colour + CoC (Karis-weighted to keep bright snow from flickering)
    float4 FragPrefilter (v2f i) : SV_Target
    {
        float3 duv = _MainTex_TexelSize.xyx * float3(0.5, 0.5, -0.5);
        float2 uv0 = i.uv - duv.xy, uv1 = i.uv - duv.zy, uv2 = i.uv + duv.zy, uv3 = i.uv + duv.xy;
        float3 c0 = tex2D(_MainTex, uv0).rgb, c1 = tex2D(_MainTex, uv1).rgb;
        float3 c2 = tex2D(_MainTex, uv2).rgb, c3 = tex2D(_MainTex, uv3).rgb;
        float k0 = CoC(uv0), k1 = CoC(uv1), k2 = CoC(uv2), k3 = CoC(uv3);
        float w0 = 1.0 / (Max3(c0) + 1.0), w1 = 1.0 / (Max3(c1) + 1.0);
        float w2 = 1.0 / (Max3(c2) + 1.0), w3 = 1.0 / (Max3(c3) + 1.0);
        float3 avg = (c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3) / (w0 + w1 + w2 + w3);
        float cMin = min(min(k0, k1), min(k2, k3)), cMax = max(max(k0, k1), max(k2, k3));
        float coc = (-cMin > cMax) ? cMin : cMax;
        return float4(avg, Encode(coc));
    }

    // 1: disc bokeh, half res
    float4 FragBokeh (v2f i) : SV_Target
    {
        float4 s0 = tex2D(_MainTex, i.uv);
        float coc0 = DecodeHalfRadius(s0.a);
        float maxR = _TelescopeLens.w * 0.25;
        float4 bg = 0, fg = 0;
        const float margin = 1.0;
        for (int k = 0; k < KernelCount; k++)
        {
            float2 disp = Kernel[k] * maxR;
            float dist = length(disp);
            float4 s = tex2D(_MainTex, i.uv + disp * _MainTex_TexelSize.xy);
            float sc = DecodeHalfRadius(s.a);
            float bgCoC = max(min(coc0, sc), 0.0);
            float bw = saturate((bgCoC - dist + margin) / margin);
            float fw = saturate((-sc - dist + margin) / margin) * step(0.5, -sc);
            bg += float4(s.rgb, 1.0) * bw;
            fg += float4(s.rgb, 1.0) * fw;
        }
        bg.rgb /= bg.a + (bg.a == 0.0);
        fg.rgb /= fg.a + (fg.a == 0.0);
        float fa = saturate(fg.a * UNITY_PI / KernelCount);
        // With no background gather (in-focus centre) keep the centre colour.
        float3 bgc = bg.a > 0.0 ? bg.rgb : s0.rgb;
        return float4(lerp(bgc, fg.rgb, fa), fa);
    }

    // 2: tent filter, half res
    float4 FragPost (v2f i) : SV_Target
    {
        float4 duv = _MainTex_TexelSize.xyxy * float4(0.5, 0.5, -0.5, 0);
        float4 a = tex2D(_MainTex, i.uv - duv.xy), b = tex2D(_MainTex, i.uv - duv.zy);
        float4 c = tex2D(_MainTex, i.uv + duv.zy), d = tex2D(_MainTex, i.uv + duv.xy);
        return (a + b + c + d) * 0.25;
    }

    // 3: combine with the full-resolution source
    float4 FragCombine (v2f i) : SV_Target
    {
        float4 src = tex2D(_MainTex, i.uv);
        float4 dof = tex2D(_DofTex, i.uv);
        float coc = CoC(i.uv);
        // far field: fade from the sharp source into the blurred image between 1.5 and 4 px of blur
        float ffa = smoothstep(1.5, 4.0, coc);
        // near field: in-focus-or-near pixels take the foreground bleed; blurred near pixels own it
        float nfa = smoothstep(1.5, 4.0, -coc);
        float alpha = saturate(max(ffa, nfa) + dof.a - max(ffa, nfa) * dof.a);
        return float4(lerp(src.rgb, dof.rgb, alpha), 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragPrefilter
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragBokeh
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragPost
            #pragma target 3.0
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragCombine
            #pragma target 3.0
            ENDCG }
    }
    FallBack Off
}
