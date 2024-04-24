using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class FrustumCullingPass : IndirectPass
    {
        IndirectRenderSetting _setting;

        ComputeShader _frustumCullingCS;
        int _frustumCullingKernel;

        DispatchHelper _dispatchHelper;

        GraphicsBuffer _instanceIndexInputBuffer; // connected
        GraphicsBuffer _instanceIndexOutputBuffer; // connected

        static readonly int s_instanceDescriptorBufferID = Shader.PropertyToID("InstanceDescriptorBuffer");
        static readonly int s_instanceIndexInputBufferID = Shader.PropertyToID("InstanceIndexInputBuffer");
        static readonly int s_instanceIndexOutputBufferID = Shader.PropertyToID("InstanceIndexOutputBuffer");

        public void Init(IndirectRenderSetting setting, ComputeShader frustumCullingCS, DispatchHelper dispatchHelper)
        {
            _setting = setting;

            _frustumCullingCS = frustumCullingCS;
            _frustumCullingKernel = _frustumCullingCS.FindKernel("FrustumCulling");

            _dispatchHelper = dispatchHelper;
        }

        public void Dispose()
        {
            //
        }

        public void SetEnable(bool enable)
        {
            if (enable)
                _frustumCullingCS.DisableKeyword("_DISABLE");
            else
                _frustumCullingCS.EnableKeyword("_DISABLE");
        }

        public GraphicsBuffer GetInstanceIndexOuutputBuffer()
        {
            return _instanceIndexOutputBuffer;
        }

        public void Prepare(IndirectRenderUnmanaged* _unmanaged)
        {
            //
        }

        public void ConnectBuffer(GraphicsBuffer instanceIndexInputBuffer, GraphicsBuffer instanceIndexOutputBuffer, GraphicsBuffer instanceDescriptorBuffer)
        {
            _instanceIndexInputBuffer = instanceIndexInputBuffer;
            _instanceIndexOutputBuffer = instanceIndexOutputBuffer;

            _frustumCullingCS.SetBuffer(_frustumCullingKernel, s_instanceIndexInputBufferID, instanceIndexInputBuffer);
            _frustumCullingCS.SetBuffer(_frustumCullingKernel, s_instanceIndexOutputBufferID, instanceIndexOutputBuffer);
            _frustumCullingCS.SetBuffer(_frustumCullingKernel, s_instanceDescriptorBufferID, instanceDescriptorBuffer);
        }

        static readonly ProfilerMarker s_frustumCullingMarker = new ProfilerMarker("FrustumCulling");
        public void BuildCommandBuffer(CommandBuffer cmd, CullingHelper cullingHelper)
        {
            cmd.BeginSample(s_frustumCullingMarker);

            //cullingHelper.SetShaderParams(cmd, _frustumCullingCS);

            _dispatchHelper.AdjustThreadGroupX(cmd, _instanceIndexInputBuffer);
            _dispatchHelper.Dispatch(cmd, _frustumCullingCS, _frustumCullingKernel);

            cmd.EndSample(s_frustumCullingMarker);
        }
    }
}