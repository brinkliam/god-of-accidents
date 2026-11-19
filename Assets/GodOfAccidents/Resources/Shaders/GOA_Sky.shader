// God of Accidents — procedural painterly sky: gradient, sun disc and halo, moon, twinkling stars, horizon glow.
Shader "GOA/Sky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _GOA_SkyZenith;
            half4 _GOA_SkyHorizon;
            half4 _GOA_SkyGround;
            half4 _GOA_SunColor;
            float4 _GOA_SunDir;
            float4 _GOA_MoonDir;
            half _GOA_Night;
            half _GOA_Storm;

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                half3 col;
                if (y > 0)
                {
                    float t = pow(saturate(y), 0.45);
                    col = lerp(_GOA_SkyHorizon.rgb, _GOA_SkyZenith.rgb, t);
                }
                else
                {
                    col = lerp(_GOA_SkyHorizon.rgb, _GOA_SkyGround.rgb, saturate(-y * 4));
                }

                float3 sd = normalize(_GOA_SunDir.xyz);
                float sdot = dot(d, sd);
                // warm halo hugging the horizon near the sun
                float horizonBand = exp(-abs(y) * 6);
                col += _GOA_SunColor.rgb * pow(saturate(sdot * 0.5 + 0.5), 6) * horizonBand * 0.55;
                col += _GOA_SunColor.rgb * pow(saturate(sdot), 64) * 0.6;
                col += _GOA_SunColor.rgb * smoothstep(0.9993, 0.9997, sdot) * 6 * (1 - _GOA_Storm);

                // moon
                float3 md = normalize(_GOA_MoonDir.xyz);
                float mdot = dot(d, md);
                col += half3(0.75, 0.8, 0.95) * smoothstep(0.9986, 0.9991, mdot) * 1.2 * _GOA_Night;
                col += half3(0.25, 0.3, 0.45) * pow(saturate(mdot), 40) * 0.35 * _GOA_Night;

                // stars
                if (_GOA_Night > 0.01 && y > 0)
                {
                    float3 cell = floor(d * 220);
                    float h = Hash(cell);
                    float star = step(0.9965, h);
                    float tw = 0.6 + 0.4 * sin(_Time.y * (2 + h * 5) + h * 40);
                    col += star * tw * _GOA_Night * saturate(y * 3) * (1 - _GOA_Storm) * 1.4;
                    // a faint milky band
                    float band = exp(-pow(dot(d, normalize(float3(0.3, 0.6, 0.74))) * 4, 2));
                    col += half3(0.12, 0.12, 0.2) * band * _GOA_Night * saturate(y * 2) * (1 - _GOA_Storm);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
