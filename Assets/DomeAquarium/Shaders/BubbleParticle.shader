Shader "DomeAquarium/Bubble Particle"
{
    Properties
    {
        _BaseColor("Bubble Tint", Color) = (0.65, 0.92, 1.0, 0.72)
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

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "BubbleParticle"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 파티클 쿼드 안에서 원을 직접 계산한다. 텍스처 압축이나 알파 설정과 무관하게
                // 모서리는 항상 완전히 투명하므로 흰 사각형이 나타날 수 없다.
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);

                half outer = 1.0h - smoothstep(0.82h, 1.0h, radius);
                half inner = smoothstep(0.52h, 0.76h, radius);
                half rim = outer * inner;

                // 왼쪽 위에 작은 반사광을 두어 평면 원이 아니라 얇은 거품막처럼 보이게 한다.
                float highlightDistance = distance(input.uv, float2(0.34, 0.68));
                half highlight = (1.0h - smoothstep(0.035h, 0.13h, highlightDistance)) * outer;
                half centerFilm = (1.0h - smoothstep(0.0h, 0.78h, radius)) * 0.055h;

                half alpha = saturate((rim * 0.62h + highlight * 0.55h + centerFilm)
                                      * input.color.a * _BaseColor.a);
                clip(alpha - 0.002h);

                half3 color = input.color.rgb * _BaseColor.rgb;
                color = lerp(color, half3(1.0h, 1.0h, 1.0h), highlight * 0.65h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
