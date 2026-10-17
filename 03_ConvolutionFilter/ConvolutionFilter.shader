Shader "Hidden/Custom/ConvolutionFilter"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ConvolutionFilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 5x5 커널, 행 우선, 0행 = 화면 위쪽 (3x3은 가운데 배치)
            float _Kernel[25];
            float4 _SourceTexelSize;
            float _SampleDistance;
            float _Intensity;
            float _Grayscale;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                // UV 원점이 위쪽인 플랫폼(D3D 등)에서는 화면 위쪽이 -V — Sobel 등 방향성 커널의 위아래를 맞춤
                #if UNITY_UV_STARTS_AT_TOP
                    float upSign = -1.0;
                #else
                    float upSign = 1.0;
                #endif

                float2 texelStep = _SourceTexelSize.xy * _SampleDistance;
                float3 sum = 0.0;

                [unroll]
                for (int row = 0; row < 5; row++)
                {
                    [unroll]
                    for (int column = 0; column < 5; column++)
                    {
                        float weight = _Kernel[row * 5 + column];
                        float2 offset = float2(column - 2, (2 - row) * upSign) * texelStep;
                        float3 neighbor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + offset).rgb;
                        if (_Grayscale > 0.5)
                            neighbor = Luminance(neighbor).xxx;
                        sum += weight * neighbor;
                    }
                }

                // 참고 글과 동일하게 결과를 0~1로 자른 뒤 원본과 섞음
                float3 filtered = saturate(sum);
                return half4(lerp(source.rgb, filtered, _Intensity), source.a);
            }
            ENDHLSL
        }
    }
}
