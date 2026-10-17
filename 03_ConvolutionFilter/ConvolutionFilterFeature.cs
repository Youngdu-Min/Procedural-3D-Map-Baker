using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// URP Renderer에 추가하는 커널 합성곱 포스트 프로세싱. 실제 값은 Volume의 ConvolutionFilter 오버라이드에서 읽음
public sealed class ConvolutionFilterFeature : ScriptableRendererFeature
{
    private const string SHADER_NAME = "Hidden/Custom/ConvolutionFilter";

    [Tooltip("Hidden/Custom/ConvolutionFilter 셰이더 (빌드 포함을 위해 직접 참조)")]
    [SerializeField] private Shader m_shader;

    [Tooltip("적용 시점 — 기본값은 URP 후처리(톤매핑 등)가 끝난 최종 화면")]
    [SerializeField] private RenderPassEvent m_renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

    private Material m_material;
    private ConvolutionFilterPass m_pass;

    public override void Create()
    {
#if UNITY_EDITOR
        // Add Renderer Feature 버튼으로 추가할 때는 OnValidate가 호출되지 않아 셰이더가 비어 있을 수 있음
        if (m_shader == null) m_shader = Shader.Find(SHADER_NAME);
#endif
        m_pass = new ConvolutionFilterPass { renderPassEvent = m_renderPassEvent };

        if (m_shader == null)
            Debug.LogWarning($"[ConvolutionFilterFeature] 셰이더가 연결되지 않아 효과가 적용되지 않습니다. Shader 칸에 {SHADER_NAME}을 지정하세요.", this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_shader == null) return;
        if (m_material == null) m_material = CoreUtils.CreateEngineMaterial(m_shader);

        m_pass.Setup(m_material);
        renderer.EnqueuePass(m_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_material);
        m_material = null;
    }

#if UNITY_EDITOR
    // Renderer에 추가하는 순간 셰이더를 자동 연결 (Shader.Find는 빌드에서 보장되지 않아 에디터에서 참조로 저장)
    private void OnValidate()
    {
        if (m_shader == null) m_shader = Shader.Find(SHADER_NAME);
    }
#endif
}
