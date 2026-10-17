using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 카메라 색 버퍼 → 합성곱 머티리얼 → 새 텍스처, 이후 카메라 색을 결과 텍스처로 교체 (Render Graph)
public sealed class ConvolutionFilterPass : ScriptableRenderPass
{
    private const string PASS_NAME = "ConvolutionFilter";

    private static readonly int KernelId = Shader.PropertyToID("_Kernel");
    private static readonly int SourceTexelSizeId = Shader.PropertyToID("_SourceTexelSize");
    private static readonly int SampleDistanceId = Shader.PropertyToID("_SampleDistance");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int GrayscaleId = Shader.PropertyToID("_Grayscale");

    private Material m_material;

    public ConvolutionFilterPass()
    {
        profilingSampler = new ProfilingSampler(PASS_NAME);

        // 카메라 색을 텍스처로 읽어야 하므로 백버퍼 직접 렌더링을 막음
        requiresIntermediateTexture = true;
    }

    public void Setup(Material material) => m_material = material;

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (m_material == null) return;

        ConvolutionFilter settings = VolumeManager.instance.stack.GetComponent<ConvolutionFilter>();
        if (settings == null || !settings.IsActive()) return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (!cameraData.postProcessEnabled) return;
        if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection) return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (resourceData.isActiveTargetBackBuffer) return;

        TextureHandle source = resourceData.activeColorTexture;
        TextureDesc descriptor = renderGraph.GetTextureDesc(source);
        descriptor.name = "_ConvolutionFilterTarget";
        descriptor.clearBuffer = false;
        TextureHandle destination = renderGraph.CreateTexture(descriptor);

        // 크기가 상대값(스케일)으로 잡힌 경우 width가 0일 수 있어 카메라 타깃 크기로 대체
        int width = descriptor.width > 0 ? descriptor.width : cameraData.cameraTargetDescriptor.width;
        int height = descriptor.height > 0 ? descriptor.height : cameraData.cameraTargetDescriptor.height;

        ConvolutionKernel kernel = settings.Kernel.value;
        m_material.SetFloatArray(KernelId, ConvolutionKernels.GetWeights(kernel));
        m_material.SetFloat(GrayscaleId, ConvolutionKernels.UsesGrayscale(kernel) ? 1f : 0f);
        m_material.SetFloat(IntensityId, settings.Intensity.value);
        m_material.SetFloat(SampleDistanceId, settings.SampleDistance.value);
        m_material.SetVector(SourceTexelSizeId, new Vector4(1f / width, 1f / height, width, height));

        RenderGraphUtils.BlitMaterialParameters blitParameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, m_material, 0);
        renderGraph.AddBlitPass(blitParameters, PASS_NAME);

        resourceData.cameraColor = destination;
    }
}
