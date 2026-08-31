// WaterTankInner.shader
// 원통 수조(WaterTank, Scale 24/8/24)의 "안쪽 면"에 붙는 배경 물 셰이더.
// 플레이어는 원통 정중앙(0,0,0)에 고정되어 바깥쪽 벽을 바라본다 = 항상 메시의 뒷면을 본다.
// URP 17.3 (Unity 6) / Quest 2 스탠드얼론 / Single Pass Instanced 전용.
Shader "DomeAquarium/WaterTankInner"
{
    Properties
    {
        _ShallowColor    ("Shallow Color (수면 쪽)", Color) = (0.30, 0.72, 0.86, 1)
        _DeepColor       ("Deep Color (바닥 쪽)",   Color) = (0.015, 0.09, 0.20, 1)

        // 기본값 "bump" = 미할당 시 Unity 가 평평한 노멀(0,0,1)을 넣어 준다.
        // ("white" 로 두면 미할당 상태에서 언팩 결과가 기울어진 노멀이 되어 색이 틀어진다)
        // 타일링은 표준 _ST 를 그대로 존중한다. 권장값: _NormalA (3,3) / _NormalB (5,5).
        _NormalA         ("Normal A (권장 타일링 3,3)", 2D) = "bump" {}
        _NormalB         ("Normal B (권장 타일링 5,5)", 2D) = "bump" {}
        _NormalStrength  ("Normal Strength", Range(0, 2)) = 0.65

        _ScrollA         ("Scroll A (xy = UV/sec)", Vector) = ( 0.013, 0.030, 0, 0)
        _ScrollB         ("Scroll B (xy = UV/sec)", Vector) = (-0.021, 0.017, 0, 0)

        _DepthPower      ("Depth Power (수심 곡률)", Range(0.2, 4)) = 1.6

        _FresnelPower    ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelColor    ("Fresnel Color (a = 세기)", Color) = (0.55, 0.86, 1.0, 0.6)

        _ShaftStrength   ("Shaft Strength (빛기둥 세기)", Range(0, 2)) = 0.55
        _ShaftCount      ("Shaft Count (빛기둥 개수)",   Range(2, 24)) = 9
        _ShaftSpeed      ("Shaft Speed",                Range(0, 2)) = 0.25

        _CausticStrength ("Caustic Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        // 배경(스카이박스 대체)으로 쓰이므로 Transparent 가 아니라 Opaque 로 둔다.
        //  - 반투명이면 뒤에 아무것도 없는데 블렌딩 비용만 나가고, 물고기 200마리와
        //    정렬(sorting) 싸움이 나서 Quest 2 에서 깨진다.
        //  - Opaque + ZWrite On 이면 깊이 버퍼에 제대로 들어가 뒤쪽 오버드로가 잘린다.
        //  - 대신 Cull Front 로 "안쪽 면만" 그려서 정중앙의 플레이어가 벽을 보게 한다.
        // Queue 를 Geometry+100 으로 밀어 불투명 중 "가장 마지막"에 그린다.
        // 물고기 200마리가 먼저 깊이를 채운 뒤라 early-z 가 가려진 픽셀을 잘라 준다.
        // (이 셰이더는 fragment 가 무거운 편이라 오버드로 절감 효과가 크다)
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Opaque"
            "Queue"           = "Geometry+100"
            "IgnoreProjector" = "True"
        }

        LOD 100

        Pass
        {
            Name "WaterTankInnerForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull  Front      // 안쪽에서 보므로 앞면을 컬링한다
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            // Single Pass Instanced (multiview) : 이 pragma 가 STEREO_INSTANCING_ON 변형을 만든다.
            // 빠뜨리면 한쪽 눈에만 보이거나 두 눈이 어긋난다.
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NormalA);   SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB);   SAMPLER(sampler_NormalB);

            // SRP Batcher 호환 : 모든 머티리얼 프로퍼티를 UnityPerMaterial 에 넣는다.
            CBUFFER_START(UnityPerMaterial)
                float4 _NormalA_ST;
                float4 _NormalB_ST;
                float4 _ScrollA;
                float4 _ScrollB;
                half4  _ShallowColor;
                half4  _DeepColor;
                half4  _FresnelColor;
                half   _NormalStrength;
                half   _DepthPower;
                half   _FresnelPower;
                half   _ShaftStrength;
                half   _ShaftCount;
                half   _ShaftSpeed;
                half   _CausticStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uvAB       : TEXCOORD0;   // xy = NormalA UV, zw = NormalB UV
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
                // x,y = 오브젝트 로컬 XZ (빛기둥 각도용), z = 수심 0..1, w = fog factor
                float4 localData  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posIn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmIn = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posIn.positionCS;
                OUT.normalWS   = nrmIn.normalWS;
                OUT.viewDirWS  = GetWorldSpaceViewDir(posIn.positionWS);

                float t = _Time.y;
                OUT.uvAB.xy = IN.uv * _NormalA_ST.xy + _NormalA_ST.zw + _ScrollA.xy * t;
                OUT.uvAB.zw = IN.uv * _NormalB_ST.xy + _NormalB_ST.zw + _ScrollB.xy * t;

                // 수심 0..1 (0 = 바닥, 1 = 수면).
                // 메시의 로컬 Y 범위가 -1..1 인지 -0.5..0.5 인지에 의존하지 않도록,
                // "오브젝트 원점 기준 월드 Y" 를 "오브젝트 Y 스케일" 로 정규화한다.
                // 씬 규약: Scale.y = 8 -> 월드 y -8..+8 (높이 16m, 중심 0) 이 정확히 0..1 로 매핑된다.
                float4x4 M = GetObjectToWorldMatrix();
                float  originY = M._m13;
                float  scaleY  = length(float3(M._m01, M._m11, M._m21));
                float  h01     = saturate((posIn.positionWS.y - originY) / max(scaleY * 2.0, 1e-4) + 0.5);

                OUT.localData.xy = IN.positionOS.xz;
                OUT.localData.z  = h01;
                OUT.localData.w  = ComputeFogFactor(posIn.positionCS.z);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // ---- 텍스처 샘플 : 총 2회 (예산 4회 이하) ----------------------------
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, IN.uvAB.xy), _NormalStrength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, IN.uvAB.zw), _NormalStrength);
                // 실제 굴절은 하지 않는다(모바일 예산 초과). 탄젠트 XY 만 "물결 신호"로 재활용.
                half2 wave = nA.xy + nB.xy;                 // 미할당(flat)일 때 정확히 0 -> 깨지지 않음

                // ---- 수심 그라데이션 -------------------------------------------------
                half h01    = (half)IN.localData.z;
                half depthT = pow(max(h01, 0.0001), _DepthPower);  // pow 1회
                depthT      = saturate(depthT + wave.y * 0.06);    // 물결로 경계를 미세하게 흔든다
                half3 col   = lerp(_DeepColor.rgb, _ShallowColor.rgb, depthT);

                // ---- 빛기둥 (god rays) ----------------------------------------------
                // 원통 각도 theta 로 세로 띠를 만든다. sin 1회 + smoothstep 1회.
                // theta * count 가 최대 75 rad 까지 커지므로 half 로 하면 계단이 보인다 -> float 유지.
                float theta = atan2(IN.localData.y, IN.localData.x);   // -PI..PI
                // 개수를 정수로 반올림해야 theta 의 +-PI 이음매에서 띠가 끊기지 않는다.
                float count = floor(_ShaftCount + 0.5);
                float s     = sin(theta * count + _Time.y * _ShaftSpeed);
                half  shaft = smoothstep(0.30, 1.0, s);
                shaft      *= h01 * h01;                   // 위(수면)에서 강하고 아래로 감쇠 (pow 대신 곱)
                col        += _FresnelColor.rgb * (shaft * _ShaftStrength * 0.5);

                // ---- 코스틱 : 노멀 합성 결과 재활용 (추가 샘플 없음) -------------------
                half caus  = smoothstep(0.25, 0.90, wave.x + wave.y);
                caus      *= _CausticStrength * (0.35 + 0.65 * h01);
                col       += _ShallowColor.rgb * caus;

                // ---- 프레넬 : 안쪽에서 보므로 법선을 뒤집는다 --------------------------
                half3 N    = normalize(-IN.normalWS);      // 벽 법선은 바깥을 향하므로 반전
                half3 V    = normalize(IN.viewDirWS);
                half  ndv  = saturate(dot(N, V) + wave.x * 0.08);  // 물결로 가장자리를 살짝 일렁이게
                half  fres = pow(1.0 - ndv, _FresnelPower);        // pow 1회
                col       += _FresnelColor.rgb * (fres * _FresnelColor.a);

                col = MixFog(col, (half)IN.localData.w);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Depth Priming / Depth Prepass 대응.
        // URP 가 depth prepass 를 켜면 forward pass 가 ZTest Equal 로 덮어써지는데,
        // 이 pass 가 없으면 수조가 프리패스에 안 들어가서 "화면이 통째로 안 보인다".
        // Cull 방향은 forward pass 와 반드시 같아야 깊이가 어긋나지 않는다.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull  Front
            ZWrite On
            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   depthVert
            #pragma fragment depthFrag
            #pragma target   3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 는 "모든 pass 가 동일한 UnityPerMaterial 레이아웃"일 때만 동작한다.
            // 여기서 쓰지 않더라도 forward pass 와 똑같이 선언해 둬야 배칭이 깨지지 않는다.
            CBUFFER_START(UnityPerMaterial)
                float4 _NormalA_ST;
                float4 _NormalB_ST;
                float4 _ScrollA;
                float4 _ScrollB;
                half4  _ShallowColor;
                half4  _DeepColor;
                half4  _FresnelColor;
                half   _NormalStrength;
                half   _DepthPower;
                half   _FresnelPower;
                half   _ShaftStrength;
                half   _ShaftCount;
                half   _ShaftSpeed;
                half   _CausticStrength;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings depthVert (DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 depthFrag (DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
