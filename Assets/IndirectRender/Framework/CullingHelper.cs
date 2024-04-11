using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class CullingHelper
    {
        BatchCullingViewType _viewType;

        int[] _cullingParameters = new int[4] { 0, 0, 0, 0 };
        int[] _packedPlaneOffset = new int[4] { 0, 0, 0, 0 };
        int[] _packedPlaneCount = new int[4] { 0, 0, 0, 0 };
        Vector4[] _cameraPackedPlanes = new Vector4[Utility.c_MaxPackedPlaneCount * 4];
        Vector4[] _shadowPackedPlanes = new Vector4[Utility.c_MaxPackedPlaneCount * 4];

        public static readonly int s_CullingParametersID = Shader.PropertyToID("_CullingParameters");
        public static readonly int s_PackedPlaneOffsetID = Shader.PropertyToID("_PackedPlaneOffset");
        public static readonly int s_PackedPlaneCountID = Shader.PropertyToID("_PackedPlaneCount");
        public static readonly int s_PackedPlanesID = Shader.PropertyToID("_PackedPlanes");

        public void Init()
        {
            //
        }

        public void Dispose()
        {
            //
        }

        public void UpdateCullingParameters(ref BatchCullingContext cullingContext)
        {
            _viewType = cullingContext.viewType;

            CullingPlanes cullingPlanes = CullingUtility.CalculateCullingParameters(ref cullingContext, Allocator.Temp);
            UnsafeList<UnsafeList<PlanePacket4>> packedPlanes = CullingUtility.BuildPlanePackets(ref cullingPlanes, Allocator.Temp);

            if (_viewType == BatchCullingViewType.Camera)
            {
                Utility.Assert(packedPlanes.Length == 1);

                PopulateManagedPlanes(packedPlanes, _cameraPackedPlanes);

                _cullingParameters[0] = 0;
                _cullingParameters[1] = 1;

                _packedPlaneOffset[0] = 0;
                _packedPlaneCount[0] = packedPlanes[0].Length;

            }
            else if (_viewType == BatchCullingViewType.Light)
            {
                Utility.Assert(packedPlanes.Length <= 4);

                PopulateManagedPlanes(packedPlanes, _shadowPackedPlanes);

                _cullingParameters[0] = 1;
                _cullingParameters[1] = packedPlanes.Length;

                int offset = 0;
                for (int i = 0; i < packedPlanes.Length; ++i)
                {
                    _packedPlaneOffset[i] = offset;
                    _packedPlaneCount[i] = packedPlanes[i].Length;

                    offset += packedPlanes[i].Length;
                }
            }
        }

        public BatchCullingViewType GetViewType()
        {
            return _viewType;
        }

        void PopulateManagedPlanes(UnsafeList<UnsafeList<PlanePacket4>> packedPlanes, Vector4[] managedPlanes)
        {
            int packedPlaneCount = 0;
            for (int i = 0; i < packedPlanes.Length; ++i)
            {
                for (int j = 0; j < packedPlanes[i].Length; ++j)
                {
                    managedPlanes[packedPlaneCount * 4 + 0] = packedPlanes[i][j].Xs;
                    managedPlanes[packedPlaneCount * 4 + 1] = packedPlanes[i][j].Ys;
                    managedPlanes[packedPlaneCount * 4 + 2] = packedPlanes[i][j].Zs;
                    managedPlanes[packedPlaneCount * 4 + 3] = packedPlanes[i][j].Distances;

                    packedPlaneCount++;
                }
            }
        }

        public void SetShaderParams(CommandBuffer cmd, ComputeShader computeShader)
        {
            cmd.SetComputeIntParams(computeShader, s_CullingParametersID, _cullingParameters);
            cmd.SetComputeIntParams(computeShader, s_PackedPlaneOffsetID, _packedPlaneOffset);
            cmd.SetComputeIntParams(computeShader, s_PackedPlaneCountID, _packedPlaneCount);

            if (_viewType == BatchCullingViewType.Camera)
            {
                cmd.SetComputeVectorArrayParam(computeShader, s_PackedPlanesID, _cameraPackedPlanes);
            }
            else if (_viewType == BatchCullingViewType.Light)
            {
                cmd.SetComputeVectorArrayParam(computeShader, s_PackedPlanesID, _shadowPackedPlanes);
            }
        }
    }
}