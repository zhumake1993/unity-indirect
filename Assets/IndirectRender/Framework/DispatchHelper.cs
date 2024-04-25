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

        public static readonly int s_DispatchArgsBufferID = Shader.PropertyToID("DispatchArgsBuffer");

        public void Init(ComputeShader adjustDispatchArgCS)
        {
            _adjustDispatchArgCS = adjustDispatchArgCS;
            _adjustDispatchArgKernel = _adjustDispatchArgCS.FindKernel("AdjustDispatchArg");

            _dispatchArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments, 3, Utility.c_SizeOfInt);
            _dispatchArgsBuffer.SetData(new int[3] { 1, 1, 1 });

            _adjustDispatchArgCS.SetBuffer(_adjustDispatchArgKernel, s_DispatchArgsBufferID, _dispatchArgsBuffer);
        }

        public void Dispose()
        {
            _dispatchArgsBuffer.Dispose();
        }

        public void AdjustThreadGroupX(CommandBuffer cmd, GraphicsBuffer counterBuffer)
        {
            cmd.CopyCounterValue(counterBuffer, _dispatchArgsBuffer, 0);
            cmd.DispatchCompute(_adjustDispatchArgCS, _adjustDispatchArgKernel, 1, 1, 1);
        }

        public void Dispatch(CommandBuffer cmd, ComputeShader computeShader, int kernelIndex)
        {
            cmd.DispatchCompute(computeShader, kernelIndex, _dispatchArgsBuffer, 0);
        }
    }
}