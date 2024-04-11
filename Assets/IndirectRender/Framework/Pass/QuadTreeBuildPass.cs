using System;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public unsafe class QuadTreeBuildPass : IndirectPass
    {
        QuadTreeSetting _setting;

        ComputeShader _quadTreeBuildCS;
        int _quadTreeBuildNodeKernel;
        int _quadTreeBuildSubNodeKernel;

        DispatchHelper _dispatchHelper;

        GraphicsBuffer _quadTreeNodeIndexPingBuffer;
        GraphicsBuffer _quadTreeNodeIndexPongBuffer;
        GraphicsBuffer _quadTreeNodeVisibilityBuffer;

        int4[] _quadTreeMaxLodNodeCoords;
        int[] _quadTreeParam = new int[4] { 0, 0, 0, 0 };

        static readonly int s_quadTreeWorldOriginID = Shader.PropertyToID("_QuadTreeWorldOrigin");
        static readonly int s_quadTreeMaxLodRangeID = Shader.PropertyToID("_QuadTreeMaxLodRange");
        static readonly int s_quadTreeLodParamID = Shader.PropertyToID("_QuadTreeLodParam");
        static readonly int s_quadTreeLodOffsetID = Shader.PropertyToID("_QuadTreeLodOffset");
        static readonly int s_quadTreeNodeIndexInputBufferID = Shader.PropertyToID("QuadTreeNodeIndexInputBuffer");
        static readonly int s_quadTreeNodeIndexOutputBufferID = Shader.PropertyToID("QuadTreeNodeIndexOutputBuffer");
        static readonly int s_quadTreeNodeVisibilityBufferID = Shader.PropertyToID("QuadTreeNodeVisibilityBuffer");

        public void Init(IndirectRenderSetting setting, ComputeShader quadTreeBuildCS, DispatchHelper dispatchHelper)
        {
            _setting = setting.QuadTreeSetting;

            _quadTreeBuildCS = quadTreeBuildCS;
            _quadTreeBuildNodeKernel = _quadTreeBuildCS.FindKernel("QuadTreeBuildNode");
            _quadTreeBuildSubNodeKernel = _quadTreeBuildCS.FindKernel("QuadTreeBuildSubNode");

            _dispatchHelper = dispatchHelper;

            float[] quadTreeWorldOrigin = new float[4];
            quadTreeWorldOrigin[0] = _setting.WorldOrigin.x;
            quadTreeWorldOrigin[1] = _setting.WorldOrigin.y;
            quadTreeWorldOrigin[2] = _setting.WorldOrigin.z;
            quadTreeWorldOrigin[3] = 0;
            _quadTreeBuildCS.SetFloats(s_quadTreeWorldOriginID, quadTreeWorldOrigin);

            int[] quadTreeMaxLodRange = new int[4];
            quadTreeMaxLodRange[0] = _setting.MaxLodRange.x;
            quadTreeMaxLodRange[1] = _setting.MaxLodRange.y;
            quadTreeMaxLodRange[2] = _setting.MaxLodRange.z;
            quadTreeMaxLodRange[3] = 0;
            _quadTreeBuildCS.SetInts(s_quadTreeMaxLodRangeID, quadTreeMaxLodRange);

            int maxLodNodeNum = _setting.MaxLodRange.x * _setting.MaxLodRange.y * _setting.MaxLodRange.z;

            _quadTreeMaxLodNodeCoords = new int4[maxLodNodeNum];
            for (int z = 0; z < _setting.MaxLodRange.z; ++z)
            {
                for (int y = 0; y < _setting.MaxLodRange.y; ++y)
                {
                    for (int x = 0; x < _setting.MaxLodRange.x; ++x)
                    {
                        int index = y * _setting.MaxLodRange.x * _setting.MaxLodRange.z + z * _setting.MaxLodRange.x + x;
                        _quadTreeMaxLodNodeCoords[index] = new int4(x, y, z, 0);
                    }
                }
            }

            _quadTreeParam[0] = 0;
            _quadTreeParam[1] = _setting.MaxLod;
            _quadTreeParam[2] = 0;
            _quadTreeParam[3] = 0;

            int[] quadTreeLodOffset = CalculateQuadTreeLodOffset(out int totalLodNodeNum);
            _quadTreeBuildCS.SetInts(s_quadTreeLodOffsetID, quadTreeLodOffset);

            int lod0NodeNum = (int)math.pow(4, _setting.MaxLod) * maxLodNodeNum;

            _quadTreeNodeIndexPingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter,
                lod0NodeNum, Utility.c_SizeOfInt4);
            _quadTreeNodeIndexPongBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Counter,
                lod0NodeNum, Utility.c_SizeOfInt4);
            _quadTreeNodeVisibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                totalLodNodeNum, Utility.c_SizeOfInt4);

            _quadTreeBuildCS.SetBuffer(_quadTreeBuildNodeKernel, s_quadTreeNodeVisibilityBufferID, _quadTreeNodeVisibilityBuffer);
            _quadTreeBuildCS.SetBuffer(_quadTreeBuildSubNodeKernel, s_quadTreeNodeVisibilityBufferID, _quadTreeNodeVisibilityBuffer);
        }

        public void Dispose()
        {
            _quadTreeNodeIndexPingBuffer.Dispose();
            _quadTreeNodeIndexPongBuffer.Dispose();
            _quadTreeNodeVisibilityBuffer.Dispose();
        }

        public GraphicsBuffer GetQuadTreeNodeVisibilityBuffer()
        {
            return _quadTreeNodeVisibilityBuffer;
        }

        public int[] CalculateQuadTreeLodOffset(out int totalLodNodeNum)
        {
            int[] quadTreeLodOffset = new int[Utility.c_QuadTreeMaxLodNum * 4];
            totalLodNodeNum = 0;
            int currLodNodeNum = _quadTreeMaxLodNodeCoords.Length;
            for (int lod = _setting.MaxLod; lod >= 0; --lod)
            {
                quadTreeLodOffset[lod * 4] = totalLodNodeNum;

                totalLodNodeNum += currLodNodeNum;
                currLodNodeNum *= 4;
            }

            return quadTreeLodOffset;
        }

        public void CalculateQuadTreeNodeCoordAndLod(AABB aabb, out int4 nodeCoordAndLod, out int4 subNodeMask)
        {
            nodeCoordAndLod = new int4(0, 0, 0, -1);
            subNodeMask = new int4(0, 0, 0, 0);

            float3 aabbMax = aabb.Max;
            float3 aabbMin = aabb.Min;

            float maxLodNodeSize = math.pow(2, _setting.MaxLod) * Utility.c_QuadTreeLod0NodeSize;

            if (aabbMin.x < _setting.WorldOrigin.x || aabbMax.x > _setting.WorldOrigin.x + _setting.MaxLodRange.x * maxLodNodeSize)
                return;

            if (aabbMin.y < _setting.WorldOrigin.y || aabbMax.y > _setting.WorldOrigin.y + _setting.MaxLodRange.y * Utility.c_QuadTreeNodeHeight)
                return;

            if (aabbMin.z < _setting.WorldOrigin.z || aabbMax.z > _setting.WorldOrigin.z + _setting.MaxLodRange.z * maxLodNodeSize)
                return;

            int maxIndexY = (int)((aabb.Center.y + aabb.Extents.y - _setting.WorldOrigin.y) / Utility.c_QuadTreeNodeHeight);
            int minIndexY = (int)((aabb.Center.y - aabb.Extents.y - _setting.WorldOrigin.y) / Utility.c_QuadTreeNodeHeight);
            if (maxIndexY != minIndexY)
                return;

            nodeCoordAndLod.y = maxIndexY;

            int lodNodeSize = (int)math.pow(2, _setting.MaxLod) * Utility.c_QuadTreeLod0NodeSize;
            for (int lod = _setting.MaxLod; lod >= 0; --lod)
            {
                int3 maxIndex = (int3)((aabbMax - _setting.WorldOrigin) / lodNodeSize);
                int3 minIndex = (int3)((aabbMin - _setting.WorldOrigin) / lodNodeSize);
                if (maxIndex.x != minIndex.x || maxIndex.z != minIndex.z)
                    return;

                nodeCoordAndLod.x = maxIndex.x;
                nodeCoordAndLod.z = maxIndex.z;
                nodeCoordAndLod.w = lod;

                if (lod == 0)
                {
                    int3 subNodeMaxIndex = (int3)((aabbMax - _setting.WorldOrigin) / Utility.c_QuadTreeSubNodeSize) % Utility.c_QuadTreeSubNodeRange;
                    int3 subNodeMinIndex = (int3)((aabbMin - _setting.WorldOrigin) / Utility.c_QuadTreeSubNodeSize) % Utility.c_QuadTreeSubNodeRange;

                    UInt64 mask = 0;
                    for (int z = subNodeMinIndex.z; z <= subNodeMaxIndex.z; z++)
                    {
                        for (int x = subNodeMinIndex.x; x <= subNodeMaxIndex.x; x++)
                        {
                            int subNodeIndex = z * Utility.c_QuadTreeSubNodeRange + x;
                            mask |= ((UInt64)1 << subNodeIndex);
                        }
                    }

                    subNodeMask.x = (int)((mask >> 32) & 0xFFFFFFFF);
                    subNodeMask.y = (int)(mask & 0xFFFFFFFF);
                }

                lodNodeSize /= 2;
            }
        }

        public void Prepare(IndirectRenderUnmanaged* _unmanaged)
        {
            //
        }

        static readonly ProfilerMarker s_quadTreeBuildMarker = new ProfilerMarker("QuadTreeBuild");
        public void BuildCommandBuffer(CommandBuffer cmd, CullingHelper cullingHelper)
        {
            cmd.BeginSample(s_quadTreeBuildMarker);

            cullingHelper.SetShaderParams(cmd, _quadTreeBuildCS);

            GraphicsBuffer pingBuffer = _quadTreeNodeIndexPingBuffer;
            GraphicsBuffer pongBuffer = _quadTreeNodeIndexPongBuffer;

            int quadTreeMaxLodNodeCount = _quadTreeMaxLodNodeCoords.Length;
            int threadGroupsX = (quadTreeMaxLodNodeCount + 63) / 64;
            _dispatchHelper.SetThreadGroupX(threadGroupsX);

            pingBuffer.SetCounterValue((uint)quadTreeMaxLodNodeCount);
            pongBuffer.SetCounterValue(0);
            pingBuffer.SetData(_quadTreeMaxLodNodeCoords, 0, 0, quadTreeMaxLodNodeCount);

            for (int lod = _setting.MaxLod; lod >= 0; --lod)
            {
                _quadTreeParam[0] = lod;
                cmd.SetComputeIntParams(_quadTreeBuildCS, s_quadTreeLodParamID, _quadTreeParam);

                cmd.SetComputeBufferParam(_quadTreeBuildCS, _quadTreeBuildNodeKernel, s_quadTreeNodeIndexInputBufferID, pingBuffer);
                cmd.SetComputeBufferParam(_quadTreeBuildCS, _quadTreeBuildNodeKernel, s_quadTreeNodeIndexOutputBufferID, pongBuffer);

                _dispatchHelper.Dispatch(cmd, _quadTreeBuildCS, _quadTreeBuildNodeKernel);

                if (lod > 0)
                {
                    cmd.SetBufferCounterValue(pingBuffer, 0);

                    _dispatchHelper.AdjustThreadGroupX(cmd, pongBuffer);

                    GraphicsBuffer temp = pingBuffer;
                    pingBuffer = pongBuffer;
                    pongBuffer = temp;
                }
            }

            cmd.SetComputeBufferParam(_quadTreeBuildCS, _quadTreeBuildSubNodeKernel, s_quadTreeNodeIndexOutputBufferID, pongBuffer);

            _dispatchHelper.CopyThreadGroupX(cmd, pongBuffer);
            _dispatchHelper.Dispatch(cmd, _quadTreeBuildCS, _quadTreeBuildSubNodeKernel);

            cmd.EndSample(s_quadTreeBuildMarker);
        }

        public void DrawGizmo()
        {
            int maxLodNodeNum = _setting.MaxLodRange.x * _setting.MaxLodRange.y * _setting.MaxLodRange.z;

            int[] quadTreeLodOffset = new int[Utility.c_QuadTreeMaxLodNum];
            int totalLodNodeNum = 0;
            int currLodNodeNum = maxLodNodeNum;
            for (int lod = _setting.MaxLod; lod >= 0; --lod)
            {
                quadTreeLodOffset[lod] = totalLodNodeNum;

                totalLodNodeNum += currLodNodeNum;
                currLodNodeNum *= 4;
            }

            int4[] visibility = new int4[totalLodNodeNum];
            _quadTreeNodeVisibilityBuffer.GetData(visibility);

            for (int z = 0; z < _setting.MaxLodRange.z; ++z)
            {
                for (int y = 0; y < _setting.MaxLodRange.y; ++y)
                {
                    for (int x = 0; x < _setting.MaxLodRange.x; ++x)
                    {
                        int3 coord = new int3(x, y, z);
                        DrawGizmoRecursive(_setting.MaxLod, coord, visibility, quadTreeLodOffset, _setting.MaxLodRange);
                    }
                }
            }
        }

        void DrawGizmoRecursive(int lod, int3 coord, int4[] visibility, int[] quadTreeLodOffset, int3 maxLodNodeRange)
        {
            int lodOffset = quadTreeLodOffset[lod];
            float lodNodeSize = math.pow(2, lod) * Utility.c_QuadTreeLod0NodeSize;
            int3 lodNodeRange = (int)math.pow(2, _setting.MaxLod - lod) * maxLodNodeRange;

            int nodeIndex = coord.y * lodNodeRange.x * lodNodeRange.z + coord.z * lodNodeRange.x + coord.x;
            int4 cullingResult = visibility[lodOffset + nodeIndex];

            if (cullingResult.Equals(new int4((int)IntersectResult.In)))
            {
                Gizmos.color = Color.green;
                DrawNode(coord, lodNodeSize);
            }
            else if (cullingResult.Equals(new int4((int)IntersectResult.Out)))
            {
                Gizmos.color = Color.red;
                DrawNode(coord, lodNodeSize);
            }
            else
            {
                if (lod > 0)
                {
                    if (!cullingResult.Equals(new int4((int)IntersectResult.Partial)))
                    {
                        Utility.LogError($"_quadTreeNodeVisibilityBuffer has invalid value. lod={lod},index={nodeIndex},value={cullingResult}");
                    }

                    DrawGizmoRecursive(lod - 1, new int3(coord.x * 2,     coord.y, coord.z * 2),     visibility, quadTreeLodOffset, maxLodNodeRange);
                    DrawGizmoRecursive(lod - 1, new int3(coord.x * 2 + 1, coord.y, coord.z * 2),     visibility, quadTreeLodOffset, maxLodNodeRange);
                    DrawGizmoRecursive(lod - 1, new int3(coord.x * 2,     coord.y, coord.z * 2 + 1), visibility, quadTreeLodOffset, maxLodNodeRange);
                    DrawGizmoRecursive(lod - 1, new int3(coord.x * 2 + 1, coord.y, coord.z * 2 + 1), visibility, quadTreeLodOffset, maxLodNodeRange);
                }
                else
                {
                    int3 subNodeRange = new int3(Utility.c_QuadTreeLod0NodeSize / Utility.c_QuadTreeSubNodeSize);

                    for (int z = 0; z < subNodeRange.z; ++z)
                    {
                        for (int x = 0; x < subNodeRange.x; ++x)
                        {
                            int subNodeIndex = z * (Utility.c_QuadTreeLod0NodeSize / Utility.c_QuadTreeSubNodeSize) + x;

                            if (subNodeIndex < 32)
                            {
                                int mask = 1 << subNodeIndex;
                                if ((cullingResult.w & mask) != 0)
                                {
                                    Gizmos.color = Color.blue;
                                }
                                else
                                {
                                    if ((cullingResult.y & mask) != 0)
                                    {
                                        Gizmos.color = Color.red;
                                    }
                                    else
                                    {
                                        Gizmos.color = Color.green;
                                    }
                                }
                            }
                            else
                            {
                                subNodeIndex -= 32;

                                int mask = 1 << subNodeIndex;
                                if ((cullingResult.z & mask) != 0)
                                {
                                    Gizmos.color = Color.blue;
                                }
                                else
                                {
                                    if ((cullingResult.x & mask) != 0)
                                    {
                                        Gizmos.color = Color.red;
                                    }
                                    else
                                    {
                                        Gizmos.color = Color.green;
                                    }
                                }
                            }

                            DrawSubNode(coord, new int3(x, 0, z));
                        }
                    }
                }
            }
        }

        void DrawNode(int3 coord, float lodNodeSize)
        {
            Vector3 center;
            center.x = coord.x * lodNodeSize + 0.5f * lodNodeSize + _setting.WorldOrigin.x;
            center.y = coord.y * Utility.c_QuadTreeNodeHeight + 0.5f * Utility.c_QuadTreeNodeHeight + _setting.WorldOrigin.y;
            center.z = coord.z * lodNodeSize + 0.5f * lodNodeSize + _setting.WorldOrigin.z;

            Gizmos.DrawWireCube(center, new Vector3(lodNodeSize, Utility.c_QuadTreeNodeHeight, lodNodeSize));
        }

        void DrawSubNode(int3 coord, int3 subCoord)
        {
            Vector3 center;
            center.x = coord.x * Utility.c_QuadTreeLod0NodeSize + _setting.WorldOrigin.x + subCoord.x * Utility.c_QuadTreeSubNodeSize + 0.5f * Utility.c_QuadTreeSubNodeSize;
            center.y = coord.y * Utility.c_QuadTreeNodeHeight + 0.5f * Utility.c_QuadTreeNodeHeight + _setting.WorldOrigin.y;
            center.z = coord.z * Utility.c_QuadTreeLod0NodeSize + _setting.WorldOrigin.z + subCoord.z * Utility.c_QuadTreeSubNodeSize + 0.5f * Utility.c_QuadTreeSubNodeSize;

            Gizmos.DrawWireCube(center, new Vector3(Utility.c_QuadTreeSubNodeSize, Utility.c_QuadTreeNodeHeight, Utility.c_QuadTreeSubNodeSize));
        }
    }
}