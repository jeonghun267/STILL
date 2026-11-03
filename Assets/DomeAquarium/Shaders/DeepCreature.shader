Shader "DomeAquarium/Deep Creature"
{
    // 1,000m 심해 생물(DomeAquarium/21). 관찰기 조명(ObservationLight)에 비칠 때만 제 색이 보인다.
    // Quest 용 URP 설정은 추가 조명을 꺼 두어 스포트라이트가 실제로는 아무것도 비추지 않는다 — 프로젝트 전체에 추가 조명을 켜는 대신
    // 이 셰이더만 조명의 위치 · 방향 · 원뿔을 전역 값(_DeviceLight*)으로 받아 직접 계산한다.
    // 그 밖에는 Simple Lit 과 같다: 주변광(SH) + 해(주광) + 발광 텍스처(발광기) + 물속 안개. 얇은 지느러미는 양면, 이빨 · 지느러미 끝은 잘라내기.
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.4
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        _Wrap("Light Wrap", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half _Cutoff;
            half _Wrap;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "DeepCreature"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            // ObservationLight 가 매 프레임 채운다. 꺼지면 _DeviceLightPos.w = 0.
            float4 _DeviceLightPos;      // xyz 위치, w 켜짐(0/1)
            float4 _DeviceLightDir;      // xyz 비추는 방향
            float4 _DeviceLightParams;   // x 사거리, y 바깥 원뿔 cos, z 안쪽 원뿔 cos, w 세기
            half4 _DeviceLightColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            // 감싼 램버트 — 양면 막의 뒷면 · 빛을 비스듬히 받는 면도 아주 까맣게 죽지 않게
            half Wrap(half3 n, half3 l)
            {
                return saturate((dot(n, l) + _Wrap) / (1.0h + _Wrap));
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            #if defined(_ALPHATEST_ON)
                clip(albedo.a - _Cutoff);
            #endif
                half3 n = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1.0h, -1.0h);

                half3 light = SampleSH(n);
                Light sun = GetMainLight();
                light += sun.color * Wrap(n, sun.direction);

                if (_DeviceLightPos.w > 0.5)
                {
                    float3 toLight = _DeviceLightPos.xyz - input.positionWS;
                    float dist = length(toLight);
                    float3 l = toLight / max(dist, 1e-4);
                    float range = _DeviceLightParams.x;
                    float fall = saturate(1.0 - (dist * dist) / (range * range));
                    float cone = smoothstep(_DeviceLightParams.y, _DeviceLightParams.z, dot(-l, _DeviceLightDir.xyz));
                    light += _DeviceLightColor.rgb * (_DeviceLightParams.w * fall * fall * cone * Wrap(n, (half3)l));
                }

                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                half3 color = albedo.rgb * light + emission;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            #endif
                return input.positionHCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
