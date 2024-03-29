using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class DispatchHelper
    {
        ComputeShader _adjustDispatchArgCS;
        int _adjustDispatchArgKernel;

        GraphicsBuffer _dispatchArgsBuffer;
        int[] _dispatchArgsArray = new int[3] { 1, 1, 1 };

        public static readonly int s_DispatchArgsBufferID = Shader.PropertyToID("DispatchArgsBuffer");

        public void Init(ComputeShader adjustDispatchArgCS)
        {
            _adjustDispatchArgCS = adjustDispatchArgCS;
            _adjustDispatchArgKernel = _adjustDispatchArgCS.FindKernel("AdjustDispatchArg");

            _dispatchArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments,
                3, Utility.c_SizeOfInt);

            _adjustDispatchArgCS.SetBuffer(_adjustDispatchArgKernel, s_DispatchArgsBufferID, _dispatchArgsBuffer);
        }

        public void Dispose()
        {
            _dispatchArgsBuffer.Dispose();
        }

        public GraphicsBuffer GetDispatchArgsBuffer()
        {
            return _dispatchArgsBuffer;
        }

        public void SetThreadGroupX(int threadGroupsX)
        {
            _dispatchArgsArray[0] = threadGroupsX;
            _dispatchArgsBuffer.SetData(_dispatchArgsArray);
        }

        public void CopyThreadGroupX(CommandBuffer cmd, GraphicsBuffer counterBuffer)
        {
            cmd.CopyCounterValue(counterBuffer, _dispatchArgsBuffer, 0);
        }

        public void AdjustThreadGroupX(CommandBuffer cmd, GraphicsBuffer counterBuffer)
        {
            cmd.BeginSample("AdjustThreadGroupX");

            cmd.CopyCounterValue(counterBuffer, _dispatchArgsBuffer, 0);
            cmd.DispatchCompute(_adjustDispatchArgCS, _adjustDispatchArgKernel, 1, 1, 1);

            cmd.EndSample("AdjustThreadGroupX");
        }

        public void Dispatch(CommandBuffer cmd, ComputeShader computeShader, int kernelIndex)
        {
            cmd.DispatchCompute(computeShader, kernelIndex, _dispatchArgsBuffer, 0);
        }
    }
}