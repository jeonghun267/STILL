Shader "DomeAquarium/Light Beam"
{
    // 관찰기 조명의 빛줄기. 물속에 번지는 빛처럼 아주 옅게 더한다.
    // 꼭짓점 색 알파로 길이 방향(관찰기 쪽 진하고 끝으로 갈수록 사라짐)을 받고,
    // 원뿔 면을 정면으로 볼수록 밝고 비스듬할수록 흐리게 해 가장자리 테두리가 선으로 보이지 않게 한다.
    // URP 파티클 셰이더는 일반 메시의 꼭짓점 알파를 쓰지 않아 원뿔이 불투명한 회색 통처럼 보였다 — 그래서 따로 쓴다.
    Properties
    {
        _Color("Beam Color", Color) = (0.85, 0.92, 1.0, 1)
        _Intensity("Intensity", Range(0, 4)) = 1
        _FacingPower("Facing Power", Range(0.5, 6)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LightBeam"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _FacingPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                half alpha : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.alpha = input.color.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half facing = abs(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half a = input.alpha * pow(facing, _FacingPower) * _Intensity;
                return half4(_Color.rgb * a, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
