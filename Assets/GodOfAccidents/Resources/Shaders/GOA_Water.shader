// God of Accidents — stylized water: animated waves, depth-based colour, shoreline foam, sky fresnel, sun glints.
Shader "GOA/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.32, 0.78, 0.78, 0.55)
        _Deep ("Deep", Color) = (0.05, 0.25, 0.42, 0.92)
        _Foam ("Foam", Color) = (0.95, 0.98, 1, 1)
        _DepthRange ("Depth Range", Float) = 2.5
        _FoamWidth ("Foam Width", Float) = 0.6
        _WaveHeight ("Wave Height", Float) = 0.06
        _Flow ("Flow (xz dir, w speed)", Vector) = (0, -1, 0, 0.6)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow; half4 _Deep; half4 _Foam;
                float _DepthRange; float _FoamWidth; float _WaveHeight; float4 _Flow;
            CBUFFER_END

            half4 _GOA_AmbientSky;
            half4 _GOA_SkyHorizon;
            half _GOA_Night;
            half _GOA_Snow;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half fog : TEXCOORD2;
                half4 color : COLOR;
            };

            float Waves(float2 p, float t)
            {
                return sin(p.x * 0.55 + t * 1.3) * 0.5 + sin(p.y * 0.7 - t * 1.1) * 0.35 + sin((p.x + p.y) * 1.3 + t * 2.0) * 0.15;
            }

            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws.y += Waves(ws.xz, _Time.y) * _WaveHeight;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.color = v.color;
                return o;
            }

            float Ripple(float2 p)
            {
                float2 q = p * 0.9;
                return sin(q.x * 3.1 + sin(q.y * 2.3)) * sin(q.y * 2.7 + sin(q.x * 1.9));
            }

            half4 frag(V i) : SV_Target
            {
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surfZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float depth = max(0, sceneZ - surfZ);
                half dt = saturate(depth / _DepthRange);

                float2 flow = _Flow.xy * _Time.y * _Flow.w;
                float2 p = i.positionWS.xz;
                float rip = Ripple(p * 0.6 + flow) * 0.5 + Ripple(p * 1.3 - flow * 1.7) * 0.5;
                float3 n = normalize(float3(rip * 0.12, 1, Ripple(p * 0.8 + flow.yx) * 0.12));

                half4 col = lerp(_Shallow, _Deep, dt);
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = pow(1 - saturate(dot(n, viewDir)), 4);
                col.rgb = lerp(col.rgb, _GOA_SkyHorizon.rgb, fres * 0.6);

                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 h = normalize(l.direction + viewDir);
                half spec = pow(saturate(dot(n, h)), 260) * 1.1 * l.shadowAttenuation;
                col.rgb *= lerp(0.35, 1.0, saturate(l.direction.y + 0.3)) * lerp(0.75, 1.0, l.shadowAttenuation);
                col.rgb += l.color * spec;
                col.rgb += _GOA_AmbientSky.rgb * 0.15;

                float foamEdge = saturate(1 - depth / _FoamWidth);
                float foamNoise = step(0.35, frac(rip * 2.0 + _Time.y * 0.3 + depth * 2));
                half foam = saturate(foamEdge * (0.6 + 0.4 * foamNoise)) * i.color.a;
                foam = max(foam, saturate(i.color.r - 0.5) * 2 * (0.5 + 0.5 * sin(p.y * 2 + _Time.y * 6)));
                col.rgb = lerp(col.rgb, _Foam.rgb * lerp(1, 0.4, _GOA_Night), foam);
                col.a = saturate(max(col.a, foam));
                col.rgb = lerp(col.rgb, half3(0.82, 0.88, 0.95), _GOA_Snow * 0.15);
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
    }
}
