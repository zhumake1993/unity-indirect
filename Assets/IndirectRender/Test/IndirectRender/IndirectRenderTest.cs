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
    public RenderData RenderData;
}

public class IndirectRenderTest : MonoBehaviour
{
    [Header("Rendering")]
    public Mesh[] Meshes;
    public Material[] Materials;
    public int MaxInstanceCount = 100;
    public int MaxHeight = 10;
    public ComputeShader IndirectPipelineCS;
    public ComputeShader AdjustDispatchArgCS;

    [Header("Other")]
    public uint Seed = 1234;
    public bool Draw = true;
    public bool QuadTreeCull = true;
    public bool FrustumCull = true;
    public bool DrawQuadTree = false;

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
    public static int s_IndirectPeoperty1 = Shader.PropertyToID("_IndirectPeoperty1");

    void Start()
    {
        _indirectRender = new IndirectRender();

        IndirectRenderSetting indirectRenderSetting = new IndirectRenderSetting()
        {
            IndexCapacity = 1 * 1024 * 1024,
            VertexCapacity = 1 * 1024 * 1024,
            MeshletTriangleCount = 64,

            InstanceCapacity = 1 * 1024 * 1024,
            MeshletCapacity = 4 * 1024 * 1024,
            CmdCapacity = 1024,
            BatchCapacity = 1024,

            QuadTreeSetting = new QuadTreeSetting
            {
                MaxLod = 6,
                Lod0NodeSize = 64,
                NodeHeight = 64,
                WorldOrigin = new int3(-1000, 0, -1000),
                MaxLodRange = new int3(4, 1, 4),
            },

            InstanceIndexMinCount = 16,
            InstanceIndexMaxCount = 1 * 1024 * 1024,
            MeshletIndexMinCount = 16,
            MeshletIndexMaxCount = 4 * 1024 * 1024,
            InstanceDataMinSizeBytes = 256,
            InstanceDataMaxSizeBytes = 16 * 1024 * 1024,
        };

        _indirectRender.Init(indirectRenderSetting, IndirectPipelineCS, AdjustDispatchArgCS);

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
            _meshIDs[i] = _indirectRender.RegisterMesh(Meshes[i]);
        }

        _materialIDs = new int[Materials.Length];
        for (int i = 0; i < Materials.Length; ++i)
        {
            Materials[i].EnableKeyword("ZGAME_INDIRECT");
            _materialIDs[i] = _indirectRender.RegisterMaterial(Materials[i], false);
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
            RandomEnable();
        }
        else if (_animationType == AnimationType.UpdateMatrix)
        {
            //RandomUpdateMatrix();
        }
        else if (_animationType == AnimationType.UpdateProperty)
        {
            //RandomUpdateProperty();
        }

        _indirectRender.EnableQuadTree = QuadTreeCull;
        //_indirectRender.EnableFrustumCull = FrustumCull;

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

    void Add(int meshIndex, int materialIndex, int instanceCount, int height)
    {
        int meshID = _meshIDs[meshIndex];
        int materialID = _materialIDs[materialIndex];

        if (height == -1)
            return;

        int indirectLayer = LayerMask.NameToLayer("IndirectLayer");

        RenderData renderData = new RenderData()
        {
            MeshID = meshID,
            SubmeshIndex = 0,
            MaterialID = materialID,
            Layer = (byte)indirectLayer,
            ReceiveShadows = true,
            ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
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

        if (material.HasColor(s_IndirectPeoperty1))
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

        UnsafeList<RenderData> renderDatas = new UnsafeList<RenderData>(1, Allocator.TempJob) { renderData };
        float4 lodParam = new float4(0.25f, 0.125f, 0.0625f, 0.03125f);

        int id = _indirectRender.AddBatch(renderDatas, lodParam, false, matrices, properties);

        _batchInfos.Add(new BatchInfo() { ID = id, Height = height, RenderData = renderData });
    }

    void AddRandom()
    {
        Add(_random.NextInt(0, Meshes.Length),
            _random.NextInt(0, Materials.Length), 
            _random.NextInt(1, MaxInstanceCount + 1),
            PickHeight());
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

    void RandomEnable()
    {
        if (_batchInfos.Count == 0)
            return;

        int batchIndex = _random.NextInt(0, _batchInfos.Count);
        BatchInfo batchInfo = _batchInfos[batchIndex];

        int instanceCount = _indirectRender.GetInstanceCount(batchInfo.ID);
        if (instanceCount == -1)
            return;

        int index = _random.NextInt(0, instanceCount);

        bool enable = _indirectRender.GetInstanceEnable(batchInfo.ID, index);
        _indirectRender.SetInstanceEnable(batchInfo.ID, index, !enable);
    }

    void OnGUI()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Test", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            Add(0, 0, 1, 1);
            Add(0, 1, 1, 5);
            //for (int i = 0; i < MaxHeight; ++i) Add(0, 0, MaxInstanceCount, i);
        }

        if (GUILayout.Button("TestLod", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            TestLod();
        }

        if (GUILayout.Button("TestMerge", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
        {
            for (int iTurn = 0; iTurn < 128; ++iTurn)
            {
                for (int i = 0; i < _meshIDs.Length; ++i)
                    _indirectRender.UnregisterMesh(_meshIDs[i]);
                for (int i = 0; i < _materialIDs.Length; ++i)
                    _indirectRender.UnregisterMaterial(_materialIDs[i]);

                PrepareAssets();
            }
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

    void TestLod()
    {
        int instanceCount = 1000;
        int indirectLayer = LayerMask.NameToLayer("IndirectLayer");
        float height = 1;

        UnsafeList<RenderData> renderDatas = new UnsafeList<RenderData>(3, Allocator.TempJob);
        renderDatas.Length = 3;

        for (int i = 0; i < 3; ++i)
        {
            renderDatas[i] = new RenderData
            {
                MeshID = _meshIDs[i],
                SubmeshIndex = 0,
                MaterialID = _materialIDs[0],
                Layer = (byte)indirectLayer,
                ReceiveShadows = true,
                ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
            };
        }

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

        Material material = _indirectRender.GetMaterial(_materialIDs[0]);
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

        float4 lodParam = new float4(0.25f, 0.125f, 0.0625f, 0.03125f);

        int id = _indirectRender.AddBatch(renderDatas, lodParam, false, matrices, properties);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (DrawQuadTree)
        {
            if (_indirectRender != null)
                _indirectRender.DrawQuadTree();
        }
    }
#endif

    string GetStats()
    {
        string log = "";

        IndirectRenderStats indirectRenderStats = _indirectRender.GetIndirectRenderStats();

        IndirectRenderSetting indirectRenderSetting = indirectRenderStats.IndirectRenderSetting;
        MeshMergerStats meshMergerStats = indirectRenderStats.MeshMergerStats;
        BuddyAllocatorStats instanceIndexBAStats = indirectRenderStats.InstanceIndexBAStats;
        BuddyAllocatorStats meshletIndexBAStats = indirectRenderStats.MeshletIndexBAStats;
        BuddyAllocatorStats instanceDataBAStats = indirectRenderStats.InstanceDataBAStats;

        log += "[MeshMerger]\n";
        log += $"  UnitMeshTriangleCount={meshMergerStats.MeshletTriangleCount}\n";

        log += "[Capacity]\n";
        log += $"  InstanceCapacity: {indirectRenderStats.InstanceCount}/{indirectRenderSetting.InstanceCapacity}" +
            $"({100.0f * indirectRenderStats.InstanceCount / indirectRenderSetting.InstanceCapacity}%)\n";
        log += $"  MeshletCapacity: {indirectRenderStats.MeshletCount}/{indirectRenderSetting.MeshletCapacity}" +
            $"({100.0f * indirectRenderStats.MeshletCount / indirectRenderSetting.MeshletCapacity}%)\n";
        log += $"  CmdCapacity: {indirectRenderStats.MaxCmdID + 1}/{indirectRenderSetting.CmdCapacity}" +
            $"({100.0f * (indirectRenderStats.MaxCmdID + 1) / indirectRenderSetting.CmdCapacity}%)\n";
        log += $"  BatchCapacity: {indirectRenderStats.MaxIndirectID + 1}/{indirectRenderSetting.BatchCapacity}" +
            $"({100.0f * (indirectRenderStats.MaxIndirectID + 1) / indirectRenderSetting.BatchCapacity}%)\n";

        log += "[BuddyAllocator]\n";
        log += $"  InstanceIndexAllocator: min={indirectRenderSetting.InstanceIndexMinCount}," +
            $"max={indirectRenderSetting.InstanceIndexMaxCount}," +
            $"{instanceIndexBAStats.AllocatedBytes}/{instanceIndexBAStats.TotalBytes}" +
            $"({100.0f * instanceIndexBAStats.AllocatedBytes / instanceIndexBAStats.TotalBytes}%)\n";
        log += $"  MeshletIndexAllocator: min={indirectRenderSetting.MeshletIndexMinCount}," +
            $"max={indirectRenderSetting.MeshletIndexMaxCount}," +
            $"{meshletIndexBAStats.AllocatedBytes}/{meshletIndexBAStats.TotalBytes}" +
            $"({100.0f * meshletIndexBAStats.AllocatedBytes / meshletIndexBAStats.TotalBytes}%)\n";
        log += $"  DataAllocator: min={indirectRenderSetting.InstanceDataMinSizeBytes}," +
            $"max={indirectRenderSetting.InstanceDataMaxSizeBytes}," +
            $"{instanceDataBAStats.AllocatedBytes}/{instanceDataBAStats.TotalBytes}" +
            $"({100.0f * instanceDataBAStats.AllocatedBytes / instanceDataBAStats.TotalBytes}%)\n";

        return log;
    }
}
