Shader "DomeAquarium/Glow Bands"
{
    // 3막 미확인 생물의 빗판 띠 — 빗해파리처럼 몸을 따라 무지갯빛이 흘러간다.
    // uv0.x = 띠 번호(0~1, 띠마다 색이 어긋난다), uv0.y = 앞 끝 → 뒤 끝, uv1.x = 띠 폭 방향(0~1, 가장자리가 부드럽다).
    // 색은 시간에 따라 앞에서 뒤로 흐르고, 그 위로 작은 빛 덩어리(빗판이 치는 박자)가 더 빠르게 지나간다.
    Properties
    {
        _Intensity("Intensity", Range(0, 4)) = 1
        _Speed("Hue Flow Speed", Float) = 0.35
        _Density("Hue Bands Per Row", Float) = 1.6
        _BeatSpeed("Beat Speed", Float) = 1.8
        _BeatCount("Beats Per Row", Float) = 9
        _Saturation("Saturation", Range(0, 1)) = 0.7
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
            Name "GlowBands"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
                half _Speed;
                half _Density;
                half _BeatSpeed;
                half _BeatCount;
                half _Saturation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float across : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.across = input.uv1.x;
                return output;
            }

            half3 Hue(half h)
            {
                half3 k = abs(frac(h + half3(0.0h, 0.6667h, 0.3333h)) * 6.0h - 3.0h) - 1.0h;
                return saturate(k);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half t = _Time.y;
                half hue = frac(input.uv.y * _Density - t * _Speed + input.uv.x * 0.37h);
                half3 color = lerp(half3(1.0h, 1.0h, 1.0h), Hue(hue), _Saturation);

                half beat = sin((input.uv.y * _BeatCount - t * _BeatSpeed + input.uv.x * 2.0h) * 6.2832h) * 0.5h + 0.5h;
                beat = beat * beat * beat;

                half edge = 1.0h - abs(input.across * 2.0h - 1.0h);
                edge = smoothstep(0.0h, 0.6h, edge);

                // 양 끝(입·뒤끝)으로 갈수록 가늘고 어둡게
                half ends = smoothstep(0.0h, 0.12h, input.uv.y) * smoothstep(1.0h, 0.88h, input.uv.y);

                half3 result = color * (0.25h + beat * 1.1h) * edge * ends * _Intensity;
                return half4(result, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
