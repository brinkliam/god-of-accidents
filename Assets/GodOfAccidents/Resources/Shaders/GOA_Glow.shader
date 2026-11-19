// God of Accidents — the butterfly-effect light: unlit additive ribbons and rings with travelling pulses.
Shader "GOA/Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.6, 1.1, 0.4, 1)
        _PulseSpeed ("Pulse Speed", Float) = 1.5
        _PulseCount ("Pulse Count", Float) = 4
        _EdgeSoft ("Edge Softness", Range(0.01,1)) = 0.6
        _Alpha ("Alpha", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _PulseSpeed; float _PulseCount; half _EdgeSoft; half _Alpha;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.color = v.color; return o; }
            half4 frag(V i) : SV_Target
            {
                float across = abs(i.uv.y * 2 - 1);
                half edge = saturate((1 - across) / _EdgeSoft);
                float pulse = frac(i.uv.x * _PulseCount - _Time.y * _PulseSpeed);
                half p = 0.45 + 0.55 * smoothstep(0.0, 0.15, pulse) * (1 - smoothstep(0.15, 0.6, pulse));
                half3 c = _Color.rgb * i.color.rgb * edge * p * i.color.a * _Alpha;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
