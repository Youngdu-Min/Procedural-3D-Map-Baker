using System;
using UnityEngine.Rendering;

public enum ConvolutionKernel
{
    Identity,
    EdgeDetection1,
    EdgeDetection2,
    SobelBottom,
    SobelTop,
    SobelLeft,
    SobelRight,
    Sharpen,
    Emboss,
    BoxBlur,
    GaussianBlur3x3,
    GaussianBlur5x5,
    UnsharpMasking5x5,
}

[Serializable]
public sealed class ConvolutionKernelParameter : VolumeParameter<ConvolutionKernel>
{
    public ConvolutionKernelParameter(ConvolutionKernel value, bool overrideState = false) : base(value, overrideState) { }
}

// 커널 합성곱 포스트 프로세싱 설정 — ConvolutionFilterFeature가 Volume 스택에서 읽어 적용
[Serializable, VolumeComponentMenu("Custom/Convolution Filter")]
public sealed class ConvolutionFilter : VolumeComponent, IPostProcessComponent
{
    public ConvolutionKernelParameter Kernel = new ConvolutionKernelParameter(ConvolutionKernel.Identity);

    // 원본과 필터 결과를 섞는 비율 (0 = 원본)
    public ClampedFloatParameter Intensity = new ClampedFloatParameter(0f, 0f, 1f);

    // 이웃 픽셀 샘플 간격(픽셀) — 고해상도에서 효과가 잘 보이도록 커널을 넓힘
    public ClampedIntParameter SampleDistance = new ClampedIntParameter(1, 1, 8);

    public bool IsActive() => Intensity.value > 0f && Kernel.value != ConvolutionKernel.Identity;
}
