using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using ZGame.Indirect;

public enum AnimationType
{
    None,
    AddRemove,
    EnableDisable,
    UpdateMatrix,
    UpdateProperty,
}

public struct BatchInfo
{
    public int ID;
    public int Height;
    public IndirectKey IndirectKey;
}

public class IndirectRenderTest : MonoBehaviour
{
    [Header("Rendering")]
    public Mesh[] Meshes;
    public Material[] Materials;
    public int MaxInstanceCount = 100;
    public int MaxHeight = 10;

    public ComputeShader AdjustDispatchArgCS;
    public ComputeShader QuadTreeBuildCS;
    public ComputeShader PopulateInstanceIndexCS;
    public ComputeShader QuadTreeCullingCS;
    public ComputeShader FrustumCullingCS;
    public ComputeShader PopulateVisibilityAndIndirectArgCS;


    [Header("Other")]
    public uint Seed = 1234;
    public bool Draw = true;
    public bool EnableQuadTreeCulling = true;
    public bool EnableFrustumCulling = true;
    public bool Gizmo = false;

    IndirectRender _indirectRender;
    Unity.Mathematics.Random _random;
    List<int> _heights;
    List<BatchInfo> _batchInfos;

    int[] _meshIDs;
    int[] _materialIDs;

    AnimationType _animationType = AnimationType.None;

    int _buttonSize = 100;
    GUIStyle _style;

    public static int s_IndirectPeoperty0 = Shader.PropertyToID("_IndirectPeoperty0");

    void Start()
    {
#if ZGAME_INDIRECT_SHADOW
        Debug.Log("ZGAME_INDIRECT_SHADOW is On");
#else
        Debug.Log("ZGAME_INDIRECT_SHADOW is Off");
#endif

        _indirectRender = new IndirectRender();

        IndirectRenderSetting indirectRenderSetting = new IndirectRenderSetting()
        {
            IndexCapacity = 1 * 1024 * 1024,
            VertexCapacity = 1 * 1024 * 1024,
            UnitMeshTriangleCount = 64,

            InstanceCapacity = 16 * 1024 * 1024,
            BatchCapacity = 1024,
            IndexSegmentCapacity = 256 * 1024,

            QuadTreeSetting = new QuadTreeSetting
            {
                WorldOrigin = new int3(-1000, 0, -1000),
                MaxLodRange = new int3(4, 3, 4),
                MaxLod = 6,
            },

            MinInstanceCountPerCmd = 16,
            MaxInstanceCountPerCmd = 4 * 1024,
            NumMaxInstanceCountPerCmd = 512,

            InstanceDataMinSizeBytes = 64,
            InstanceDataMaxSizeBytes = 64 * 1024,
            InstanceDataNumMaxSizeBlocks = 256,
        };

        ComputerShaderCollection computerShaderCollection = new ComputerShaderCollection()
        {
            AdjustDispatchArgCS = AdjustDispatchArgCS,
            QuadTreeBuildCS = QuadTreeBuildCS,
            PopulateInstanceIndexCS = PopulateInstanceIndexCS,
            QuadTreeCullingCS = QuadTreeCullingCS,
            FrustumCullingCS = FrustumCullingCS,
            PopulateVisibilityAndIndirectArgCS = PopulateVisibilityAndIndirectArgCS,
        };

        _indirectRender.Init(indirectRenderSetting, computerShaderCollection);

        PrepareAssets();

        _random = new Unity.Mathematics.Random(Seed);

        _heights = new List<int>();
        for (int i = 0; i < MaxHeight; ++i)
        {
            _heights.Add(i);
        }

        _batchInfos = new List<BatchInfo>();

        _style = new GUIStyle();
        _style.fontSize = 15;
        _style.normal.textColor = Color.white;
    }

    void OnDestroy()
    {
        _indirectRender.Dispose();
    }

    void PrepareAssets()
    {
        _meshIDs = new int[Meshes.Length];
        for (int i = 0; i < Meshes.Length; ++i)
        {
            MeshKey meshKey = new MeshKey()
            {
                Mesh = Meshes[i],
                SubmeshIndex = 0,
                FlipZ = false,
            };

            _meshIDs[i] = _indirectRender.RegisterMesh(meshKey);
        }

        _materialIDs = new int[Materials.Length];
        for (int i = 0; i < Materials.Length; ++i)
        {
            Materials[i].EnableKeyword("ZGAME_INDIRECT");
            _materialIDs[i] = _indirectRender.RegisterMaterial(Materials[i]);
        }
    }

    void Update()
    {
        if (_animationType == AnimationType.AddRemove)
        {
            if (_random.NextInt(0, 2) == 1)
                AddRandom();
            else
                DeleteRandom();
        }
        else if (_animationType == AnimationType.EnableDisable)
        {
            //RandomEnable();
        }
        else if (_animationType == AnimationType.UpdateMatrix)
        {
            //RandomUpdateMatrix();
        }
        else if (_animationType == AnimationType.UpdateProperty)
        {
            //RandomUpdateProperty();
        }

        _indirectRender.SetQuadTreeCullingEnable(EnableQuadTreeCulling);
        _indirectRender.SetFrustumCullingEnable(EnableFrustumCulling);

        if (Draw)
        {
            _indirectRender.Dispatch();
        }
    }

    int PickHeight(int index)
    {
        if (_heights.Count == 0)
        {
            return -1;
        }

        int height = _heights[index];
        _heights.RemoveAtSwapBack(index);

        return height;
    }

    int PickHeight()
    {
        int index = _random.NextInt(0, _heights.Count);
        return PickHeight(index);
    }

    void Add(int meshIndex, int materialIndex, int instanceCount, int heightIndex)
    {
        int meshID = _meshIDs[meshIndex];
        int materialID = _materialIDs[materialIndex];
        int height = PickHeight(heightIndex);

        if (height == -1)
            return;

        int indirectLayer = LayerMask.NameToLayer("IndirectLayer");

        IndirectKey indirectKey = new IndirectKey()
        {
            MaterialID = materialID,
            Layer = (byte)indirectLayer,
            ReceiveShadows = false,
            ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
        };

        UnsafeList<float4x4> matrices = new UnsafeList<float4x4>(instanceCount, Allocator.TempJob);
        matrices.Resize(instanceCount, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < instanceCount; ++i)
        {
            float3 translation = new float3(i * 1.5f, height * 1.5f, 0);
            quaternion rotation = quaternion.identity;
            float3 scale = new float3(1, 1, 1);
            float4x4 matrix = float4x4.TRS(translation, rotation, scale);

            matrices[i] = matrix;
        }

        Material material = _indirectRender.GetMaterial(materialID);
        int numFloat4 = 0;

        if (material.HasColor(s_IndirectPeoperty0))
            numFloat4++;

        UnsafeList<UnsafeList<float4>> properties = new UnsafeList<UnsafeList<float4>>(numFloat4, Allocator.TempJob);
        for (int i = 0; i < numFloat4; ++i)
        {
            UnsafeList<float4> float4s = new UnsafeList<float4>(instanceCount, Allocator.TempJob);
            float4s.Resize(instanceCount, NativeArrayOptions.UninitializedMemory);
            for (int j = 0; j < instanceCount; ++j)
            {
                float4s[j] = new float4(_random.NextFloat(0, 1), _random.NextFloat(0, 1), _random.NextFloat(0, 1), _random.NextFloat(0, 1));
            }

            properties.Add(float4s);
        }

        int id = _indirectRender.AddBatch(indirectKey, meshID, false, matrices, properties);

        _batchInfos.Add(new BatchInfo() { ID = id, Height = height, IndirectKey = indirectKey });
    }

    void AddRandom()
    {
        Add(_random.NextInt(0, Meshes.Length),
            _random.NextInt(0, Materials.Length), 
            _random.NextInt(1, MaxInstanceCount + 1),
            _random.NextInt(0, _heights.Count));
    }

    void DeleteRandom()
    {
        if (_batchInfos.Count == 0)
            return;

        int index = _random.NextInt(0, _batchInfos.Count);
        BatchInfo batchInfo = _batchInfos[index];
        _batchInfos.RemoveAtSwapBack(index);

        _indirectRender.RemoveBatch(batchInfo.ID);

        _heights.Add(batchInfo.Height);
    }

    void OnGUI()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Test", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            //Add(0, 0, 1000, 0);
            Add(1, 0, 1, 1);
        }

        if (GUILayout.Button("AddRandom", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            AddRandom();
        }

        if (GUILayout.Button("DeleteRandom", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            DeleteRandom();
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Add All", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            for (int i = 0; i < MaxHeight; ++i)
                AddRandom();
        }

        if (GUILayout.Button("Delete All", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            for (int i = 0; i < MaxHeight; ++i)
                DeleteRandom();
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button($"{_animationType}", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            _animationType = (AnimationType)(((int)_animationType + 1) % 5);
        }

        if (GUILayout.Button("Create", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            _indirectRender.CreateGameobject();
        }

        GUILayout.EndHorizontal();

        GUILayout.Label(GetStats(), _style);
    }

    void OnDrawGizmos()
    {
        if (Gizmo)
        {
            if (_indirectRender != null)
                _indirectRender.DrawGizmo();
        }
    }

    string GetStats()
    {
        string log = "[IndirectRender Stats]\n";

        IndirectRenderStats indirectRenderStats = _indirectRender.GetIndirectRenderStats();

        IndirectRenderSetting indirectRenderSetting = indirectRenderStats.IndirectRenderSetting;
        MeshMergerStats meshMergerStats = indirectRenderStats.MeshMergerStats;
        BuddyAllocatorStats instanceIndicesBuddyAllocatorStats = indirectRenderStats.InstanceIndicesBuddyAllocatorStats;
        BuddyAllocatorStats instanceDataBuddyAllocatorStats = indirectRenderStats.InstanceDataBuddyAllocatorStats;

        log += $"UnitMeshTriangleCount={meshMergerStats.UnitMeshTriangleCount}\n";

        log += $"IndicesAllocator: min={indirectRenderSetting.MinInstanceCountPerCmd}," +
            $"max={indirectRenderSetting.MaxInstanceCountPerCmd},nummax={indirectRenderSetting.NumMaxInstanceCountPerCmd}\n";
        log += $"DataAllocator: min={indirectRenderSetting.InstanceDataMinSizeBytes}," +
            $"max={indirectRenderSetting.InstanceDataMaxSizeBytes},nummax={indirectRenderSetting.InstanceDataNumMaxSizeBlocks}\n";

        log += $"IndexCapacity: {meshMergerStats.TotalIndexCount}/{meshMergerStats.IndexCapacity}" +
            $"({100.0f*meshMergerStats.TotalIndexCount/ meshMergerStats.IndexCapacity}%)\n";
        log += $"VertexCapacity: {meshMergerStats.TotalVertexCount}/{meshMergerStats.VertexCapacity}" +
            $"({100.0f*meshMergerStats.TotalVertexCount/ meshMergerStats.VertexCapacity}%)\n";

        log += $"InstanceCapacity: {indirectRenderStats.TotalActualInstanceCount}/{indirectRenderSetting.InstanceCapacity}" +
            $"({100.0f * indirectRenderStats.TotalActualInstanceCount / indirectRenderSetting.InstanceCapacity}%)\n";
        log += $"BatchCapacity: {indirectRenderStats.MaxIndirectID}/{indirectRenderSetting.BatchCapacity}" +
            $"({100.0f * indirectRenderStats.MaxIndirectID / indirectRenderSetting.BatchCapacity}%)\n";
        log += $"IndexSegmentCapacity: {indirectRenderStats.IndexSegmentCount}/{indirectRenderSetting.IndexSegmentCapacity}" +
            $"({100.0f * indirectRenderStats.IndexSegmentCount / indirectRenderSetting.IndexSegmentCapacity}%)\n";

        log += $"IndicesCapacity: {instanceIndicesBuddyAllocatorStats.AllocatedBytes}/{instanceIndicesBuddyAllocatorStats.TotalBytes}" +
            $"({100.0f * instanceIndicesBuddyAllocatorStats.AllocatedBytes / instanceIndicesBuddyAllocatorStats.TotalBytes}%)\n";
        log += $"DataCapacity: {instanceDataBuddyAllocatorStats.AllocatedBytes}/{instanceDataBuddyAllocatorStats.TotalBytes}" +
            $"({100.0f * instanceDataBuddyAllocatorStats.AllocatedBytes / instanceDataBuddyAllocatorStats.TotalBytes}%)\n";

        return log;
    }
}
