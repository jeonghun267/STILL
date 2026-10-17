Shader "DomeAquarium/Glow Jelly"
{
    // 3막 미확인 생물의 몸. 스스로 빛나는 반투명 젤리 — 가장자리(시선과 비스듬한 면)가 밝고 가운데는 은은하다.
    // 가산 혼합이라 캄캄한 1,000m 에서 멀리서도 보이고, 블룸 없이(XR 카메라는 후처리가 꺼져 있다) 빛나 보인다.
    // 안개를 받지 않는다 — 생물 발광은 어둠 속 10m 밖에서도 보여야 신호를 따라갈 수 있다.
    Properties
    {
        _Color("Inner Glow", Color) = (0.06, 0.16, 0.30, 1)
        _RimColor("Rim Glow", Color) = (0.40, 0.82, 1.0, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 2.2
        _Intensity("Intensity", Range(0, 4)) = 1
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
        Cull Back

        Pass
        {
            Name "GlowJelly"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
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
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half3 v = normalize(input.viewWS);
                half rim = pow(1.0h - saturate(abs(dot(n, v))), _RimPower);
                half3 color = (_Color.rgb + _RimColor.rgb * rim) * _Intensity;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
