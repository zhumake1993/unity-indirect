using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace ZGame.Indirect
{
    public struct IndirectRenderStats
    {
        public IndirectRenderSetting IndirectRenderSetting;
        public MeshMergerStats MeshMergerStats;
        public BuddyAllocatorStats InstanceIndexBAStats;
        public BuddyAllocatorStats MeshletIndexBAStats;
        public BuddyAllocatorStats InstanceDataBAStats;
        public int InstanceCount;
        public int MeshletCount;
        public int MaxCmdID;
        public int MaxIndirectID;
    }

    public unsafe partial class IndirectRender
    {
        public IndirectRenderStats GetIndirectRenderStats()
        {
            IndirectRenderStats stats = new IndirectRenderStats
            {
                IndirectRenderSetting = _unmanaged->Setting,
                MeshMergerStats = _meshMerger.GetMeshMergerStats(),
                InstanceIndexBAStats = _unmanaged->InstanceIndexAllocator.GetStats(),
                MeshletIndexBAStats = _unmanaged->MeshletIndexAllocator.GetStats(),
                InstanceDataBAStats = _unmanaged->InstanceDataAllocator.GetStats(),
                InstanceCount = _unmanaged->InstanceCount,
                MeshletCount = _unmanaged->MeshletCount,
                MaxCmdID = _unmanaged->MaxCmdID,
                MaxIndirectID = _unmanaged->MaxIndirectID,
            };

            return stats;
        }

        public bool EnableDraw
        {
            get { return _draw; }
            set { _draw = value; }
        }

        public bool EnableQuadTree
        {
            get { return _quadTree.Enable; }
            set { _quadTree.Enable = value; }
        }

        //public bool EnableFrustumCull
        //{
        //    get { return _indirectPipeline.EnableFrustumCull; }
        //    set { _indirectPipeline.EnableFrustumCull = value; }
        //}

        public int GetInstanceCount(int cmdID)
        {
            if (_unmanaged->CmdMap.ContainsKey(cmdID))
            {
                return _unmanaged->CmdDescriptorArray[cmdID].InstanceCount;
            }
            else
            {
                return -1;
            }
        }

#if UNITY_EDITOR
        public void DrawQuadTree()
        {
            _quadTree.DrawGizmo();
        }
#endif

        public void CreateGameobject()
        {
            //GameObject debugRoot = CreateGameObject("Indirect", null);

            //Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();

            //MaterialPropertyBlock mpb = new MaterialPropertyBlock();

            //float4[] shadowBuffer = new float4[(_unmanaged->Setting.InstanceDataMaxSizeBytes * _unmanaged->Setting.InstanceDataNumMaxSizeBlocks) / Utility.c_SizeOfFloat4];
            //_indirectDrawer.GetInstanceDataBuffer().GetData(shadowBuffer);

            //foreach (var userIDPair in _unmanaged->UserIdToCmdIDs)
            //{
            //    int userID = userIDPair.Key;
            //    UnsafeList<int> cmdIDs = userIDPair.Value;

            //    GameObject userGO = CreateGameObject($"userID={userID},cmdNum={cmdIDs.Length}", debugRoot);

            //    foreach (var cmdID in cmdIDs)
            //    {
            //        IndirectCmdInfo indirectCmdInfo = _unmanaged->CmdMap[cmdID];

            //        GameObject cmdGO = CreateGameObject($"cmdID={cmdID},mesh={indirectCmdInfo.MeshID},material={indirectCmdInfo.IndirectKey.MaterialID},count={indirectCmdInfo.InstanceCount}", userGO);

            //        MeshInfo meshInfo = _assetManager.GetMeshInfo(indirectCmdInfo.MeshID);
            //        List<UnitMeshInfo> unitMeshInfos = meshInfo.UnitMeshInfos;

            //        ShaderLayout shaderLayout = _assetManager.GetShaderLayout(indirectCmdInfo.IndirectKey.MaterialID);
            //        int instanceSizeF4 = shaderLayout.GetInstanceSizeF4();
            //        int propertyCount = shaderLayout.PeopertyCount;

            //        int instanceCount = indirectCmdInfo.InstanceCount;

            //        Chunk chunk = indirectCmdInfo.InstanceDataChunk;
            //        int addr = (int)chunk.AddressOf();
            //        int offsetF4 = addr / Utility.c_SizeOfFloat4;

            //        NativeArray<float4x4> matrices = new NativeArray<float4x4>(instanceCount, Allocator.Temp);
            //        for (int iInstance = 0; iInstance < instanceCount; ++iInstance)
            //        {
            //            int instanceOffsetF4 = offsetF4 + iInstance * instanceSizeF4;
            //            matrices[iInstance] = ExtractFloat4x4(shadowBuffer, instanceOffsetF4);
            //        }

            //        UnsafeList<UnsafeList<float4>> properties = new UnsafeList<UnsafeList<float4>>(propertyCount, Allocator.Temp);
            //        properties.Length = propertyCount;
            //        for (int iProperty = 0; iProperty < propertyCount; ++iProperty)
            //        {
            //            UnsafeList<float4> float4s = new UnsafeList<float4>(instanceCount, Allocator.Temp);
            //            float4s.Length = instanceCount;
            //            for (int iInstance = 0; iInstance < instanceCount; ++iInstance)
            //            {
            //                int propertyOffsetF4 = offsetF4 + iInstance * instanceSizeF4 + 3 + (shaderLayout.NeedInverse ? 3 : 0) + iProperty;
            //                float4s[iInstance] = shadowBuffer[propertyOffsetF4];
            //            }
            //            properties[iProperty] = float4s;
            //        }

            //        UnsafeList<IndirectSubCmdInfo> subCmds = indirectCmdInfo.SubCmds;
            //        for (int iSubCmd = 0; iSubCmd < subCmds.Length; ++iSubCmd)
            //        {
            //            IndirectSubCmdInfo indirectSubCmdInfo = subCmds[iSubCmd];

            //            GameObject subCmdGO = CreateGameObject($"iSubCmd={iSubCmd},start={indirectSubCmdInfo.StartInstanceIndex}", cmdGO);

            //            UnitMeshInfo unitMeshInfo = unitMeshInfos[iSubCmd];
            //            Mesh debugMesh = _meshMerger.CreateDebugMesh(unitMeshInfo);

            //            for (int iInstance = 0; iInstance < instanceCount; ++iInstance)
            //            {
            //                GameObject instanceGO = CreateGameObject($"{iInstance}", subCmdGO);
            //                instanceGO.transform.position = matrices[iInstance].ExtractPosition();
            //                instanceGO.transform.rotation = matrices[iInstance].ExtractRotation();
            //                instanceGO.transform.localScale = matrices[iInstance].ExtractScale();

            //                MeshFilter mf = instanceGO.AddComponent<MeshFilter>();
            //                MeshRenderer mr = instanceGO.AddComponent<MeshRenderer>();
            //                mf.mesh = debugMesh;

            //                Material indirectMaterial = _assetManager.GetMaterial(indirectCmdInfo.IndirectKey.MaterialID);
            //                if (!materialMap.TryGetValue(indirectMaterial, out var debugMaterial))
            //                {
            //                    debugMaterial = new Material(indirectMaterial);
            //                    debugMaterial.DisableKeyword("ZGAME_INDIRECT");
            //                    materialMap.Add(indirectMaterial, debugMaterial);
            //                }
            //                mr.material = debugMaterial;

            //                mpb.Clear();
            //                for (int iProperty = 0; iProperty < propertyCount; ++iProperty)
            //                {
            //                    mpb.SetVector(Utility.s_IndirectPeopertyIDs[iProperty], properties[iProperty][iInstance]);
            //                }
            //                mr.SetPropertyBlock(mpb);
            //            }
            //        }
            //    }
            //}
        }

        GameObject CreateGameObject(string name, GameObject parent)
        {
            GameObject go = new GameObject();

            go.name = name;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            if (parent != null)
                go.transform.parent = parent.transform;

            return go;
        }

        float4x4 ExtractFloat4x4(float4[] buffer, int offsetF4)
        {
            float4x4 matrix = new float4x4();

            matrix[0][0] = buffer[offsetF4 + 0][0];
            matrix[0][1] = buffer[offsetF4 + 0][1];
            matrix[0][2] = buffer[offsetF4 + 0][2];
            matrix[1][0] = buffer[offsetF4 + 0][3];

            matrix[1][1] = buffer[offsetF4 + 1][0];
            matrix[1][2] = buffer[offsetF4 + 1][1];
            matrix[2][0] = buffer[offsetF4 + 1][2];
            matrix[2][1] = buffer[offsetF4 + 1][3];

            matrix[2][2] = buffer[offsetF4 + 2][0];
            matrix[3][0] = buffer[offsetF4 + 2][1];
            matrix[3][1] = buffer[offsetF4 + 2][2];
            matrix[3][2] = buffer[offsetF4 + 2][3];

            matrix[0][3] = 0;
            matrix[1][3] = 0;
            matrix[2][3] = 0;
            matrix[3][3] = 1;

            return matrix;
        }
    }
}