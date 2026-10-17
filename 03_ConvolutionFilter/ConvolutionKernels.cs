using System.Collections.Generic;

// 커널 값 정의 — 셰이더는 5x5(25개, 행 우선, 0행 = 화면 위쪽)를 받으므로 3x3은 가운데에 배치
// 적힌 방향 그대로 적용(상관 연산): 참고 글에서 scipy가 커널을 뒤집기 때문에 미리 뒤집어 넘긴 것과 같은 결과
public static class ConvolutionKernels
{
    public const int KERNEL_WIDTH = 5;
    public const int WEIGHT_COUNT = KERNEL_WIDTH * KERNEL_WIDTH;

    private static readonly Dictionary<ConvolutionKernel, float[]> Weights = new Dictionary<ConvolutionKernel, float[]>
    {
        { ConvolutionKernel.Identity, Expand3x3(1f, 0, 0, 0, 0, 1, 0, 0, 0, 0) },
        { ConvolutionKernel.EdgeDetection1, Expand3x3(1f, 0, -1, 0, -1, 4, -1, 0, -1, 0) },
        { ConvolutionKernel.EdgeDetection2, Expand3x3(1f, -1, -1, -1, -1, 8, -1, -1, -1, -1) },
        { ConvolutionKernel.SobelBottom, Expand3x3(1f, -1, -2, -1, 0, 0, 0, 1, 2, 1) },
        { ConvolutionKernel.SobelTop, Expand3x3(1f, 1, 2, 1, 0, 0, 0, -1, -2, -1) },
        { ConvolutionKernel.SobelLeft, Expand3x3(1f, 1, 0, -1, 2, 0, -2, 1, 0, -1) },
        { ConvolutionKernel.SobelRight, Expand3x3(1f, -1, 0, 1, -2, 0, 2, -1, 0, 1) },
        { ConvolutionKernel.Sharpen, Expand3x3(1f, 0, -1, 0, -1, 5, -1, 0, -1, 0) },
        { ConvolutionKernel.Emboss, Expand3x3(1f, -2, -1, 0, -1, 1, 1, 0, 1, 2) },
        { ConvolutionKernel.BoxBlur, Expand3x3(1f / 9f, 1, 1, 1, 1, 1, 1, 1, 1, 1) },
        { ConvolutionKernel.GaussianBlur3x3, Expand3x3(1f / 16f, 1, 2, 1, 2, 4, 2, 1, 2, 1) },
        { ConvolutionKernel.GaussianBlur5x5, Scale(1f / 256f,
            1, 4, 6, 4, 1,
            4, 16, 24, 16, 4,
            6, 24, 36, 24, 6,
            4, 16, 24, 16, 4,
            1, 4, 6, 4, 1) },
        { ConvolutionKernel.UnsharpMasking5x5, Scale(-1f / 256f,
            1, 4, 6, 4, 1,
            4, 16, 24, 16, 4,
            6, 24, -476, 24, 6,
            4, 16, 24, 16, 4,
            1, 4, 6, 4, 1) },
    };

    public static float[] GetWeights(ConvolutionKernel kernel) => Weights[kernel];

    // 참고 글과 같이 엣지·Sobel 계열은 흑백으로 변환한 뒤 적용
    public static bool UsesGrayscale(ConvolutionKernel kernel)
        => kernel is ConvolutionKernel.EdgeDetection1 or ConvolutionKernel.EdgeDetection2
            or ConvolutionKernel.SobelBottom or ConvolutionKernel.SobelTop
            or ConvolutionKernel.SobelLeft or ConvolutionKernel.SobelRight;

    private static float[] Expand3x3(float scale, params float[] values3x3)
    {
        float[] weights = new float[WEIGHT_COUNT];
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
                weights[(row + 1) * KERNEL_WIDTH + column + 1] = values3x3[row * 3 + column] * scale;
        return weights;
    }

    private static float[] Scale(float scale, params float[] values5x5)
    {
        float[] weights = new float[WEIGHT_COUNT];
        for (int i = 0; i < WEIGHT_COUNT; i++)
            weights[i] = values5x5[i] * scale;
        return weights;
    }
}
