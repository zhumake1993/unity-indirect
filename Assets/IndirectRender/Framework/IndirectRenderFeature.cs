using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZGame.Indirect
{
    public class IndirectRenderPass : ScriptableRenderPass
    {
        public IndirectRenderPass(RenderPassEvent passEvent)
        {
            renderPassEvent = passEvent;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            if (IndirectRender.s_Instance != null)
                IndirectRender.s_Instance.IndirectDrawer.DrawIndirect(cmd);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    public class IndirectRenderFeature : ScriptableRendererFeature
    {
        IndirectRenderPass _cameraPass;
        IndirectRenderPass _shadowPass;

        public override void Create()
        {
            _cameraPass = new IndirectRenderPass(RenderPassEvent.AfterRenderingOpaques);
            _shadowPass = new IndirectRenderPass(RenderPassEvent.AfterRenderingShadows);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_cameraPass);
            //renderer.EnqueuePass(_shadowPass);
        }
    }
}