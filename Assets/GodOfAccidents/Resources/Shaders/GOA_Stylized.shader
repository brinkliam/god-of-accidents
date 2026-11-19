// God of Accidents — stylized miniature-diorama lighting.
// Vertex colours carry albedo (rgb) and wind-sway weight (a). Wrapped lambert + hemisphere ambient + rim light,
// real-time main-light shadows, additional point lights (fires, lanterns), season tint and snow.
Shader "GOA/Stylized"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        _Wind ("Wind Sway", Range(0,2)) = 0
        _Foliage ("Foliage (season tint)", Range(0,1)) = 0
        _SnowAmount ("Snow Receptivity", Range(0,1)) = 1
        _Wrap ("Light Wrap", Range(0,1)) = 0.45
        _Rim ("Rim Strength", Range(0,1)) = 0.25
        _Gloss ("Gloss", Range(0,1)) = 0.05
        _NightGlow ("Night-only emission", Range(0,1)) = 0
        _UVMask ("Use UV.x as season/snow mask", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            half _Wind;
            half _Foliage;
            half _SnowAmount;
            half _Wrap;
            half _Rim;
            half _Gloss;
            half _NightGlow;
            half _UVMask;
        CBUFFER_END

        float4 _GOA_WindDir;      // xz direction, w = strength
        half4 _GOA_SeasonTint;    // rgb tint, a = amount
        half _GOA_Snow;
        half _GOA_Night;
        half4 _GOA_AmbientSky;
        half4 _GOA_AmbientGround;
        half4 _GOA_RimColor;

        float3 ApplyWind(float3 positionWS, half weight)
        {
            if (_Wind <= 0.001 || weight <= 0.001) return positionWS;
            float phase = positionWS.x * 0.37 + positionWS.z * 0.29;
            float strength = 0.25 + _GOA_WindDir.w;
            float sway = sin(_Time.y * (1.3 + _GOA_WindDir.w) + phase) * 0.6 + sin(_Time.y * 3.1 + phase * 2.3) * 0.25;
            float2 dir = _GOA_WindDir.xz;
            positionWS.xz += dir * (sway + strength * 0.6) * _Wind * weight * 0.18;
            positionWS.y -= abs(sway) * _Wind * weight * 0.03;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
                half variation : TEXCOORD3;
                half mask : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                ws = ApplyWind(ws, v.color.a);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.mask = lerp(1.0, v.uv.x, _UVMask);
                o.fog = ComputeFogFactor(o.positionCS.z);
                float3 origin = TransformObjectToWorld(float3(0,0,0));
                o.variation = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _BaseColor.rgb;

                // per-object variation for foliage and season tint
                if (_Foliage > 0.001)
                {
                    albedo *= lerp(1.0, lerp(0.86, 1.12, i.variation), 1 - _UVMask);
                    albedo = lerp(albedo, albedo * _GOA_SeasonTint.rgb * 1.6, _GOA_SeasonTint.a * _Foliage * i.mask);
                }
                half snow = saturate((n.y - 0.45) * 2.5) * _GOA_Snow * _SnowAmount * lerp(1.0, saturate(i.mask + 0.35), _UVMask);
                albedo = lerp(albedo, half3(0.93, 0.95, 1.0), snow);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = dot(n, mainLight.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 lit = albedo * mainLight.color * wrapped * lerp(0.35, 1.0, shadow);

                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                if (_Gloss > 0.001)
                {
                    half3 h = normalize(mainLight.direction + viewDir);
                    lit += mainLight.color * pow(saturate(dot(n, h)), 8 + _Gloss * 120) * _Gloss * shadow;
                }

                half3 ambient = lerp(_GOA_AmbientGround.rgb, _GOA_AmbientSky.rgb, n.y * 0.5 + 0.5) * albedo;

                half3 extra = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    half ldot = saturate((dot(n, l.direction) + 0.6) / 1.6);
                    extra += albedo * l.color * l.distanceAttenuation * ldot;
                LIGHT_LOOP_END
                #endif

                half fres = pow(1.0 - saturate(dot(n, viewDir)), 3);
                half3 rim = _GOA_RimColor.rgb * fres * _Rim * (0.4 + wrapped);

                half3 emission = _EmissionColor.rgb * lerp(1.0, _GOA_Night, _NightGlow);
                half3 col = lit + ambient + extra + rim + emission;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vertShadow(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - ws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 fragShadow(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vertDepth(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                return o;
            }
            half4 fragDepth(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            V vertDN(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(ApplyWind(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(V i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
