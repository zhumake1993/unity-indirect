using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.IndirectExample
{
    public struct BatchKey : IEquatable<BatchKey>
    {
        public int MeshID;
        public int SubmeshIndex;
        public int MaterialID;
        public bool FlipWinding;
        public byte Layer;
        public bool ReceiveShadows;
        public ShadowCastingMode ShadowCastingMode;

        public override int GetHashCode()
        {
            return MeshID.GetHashCode()
                ^ SubmeshIndex.GetHashCode()
                ^ MaterialID.GetHashCode()
                ^ FlipWinding.GetHashCode()
                ^ Layer.GetHashCode()
                ^ ReceiveShadows.GetHashCode()
                ^ ((int)(ShadowCastingMode)).GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is BatchKey)
                return Equals((BatchKey)obj);

            return false;
        }

        public bool Equals(BatchKey other)
        {
            return MeshID == other.MeshID
                && SubmeshIndex == other.SubmeshIndex
                && MaterialID == other.MaterialID
                && FlipWinding == other.FlipWinding
                && Layer == other.Layer
                && ReceiveShadows == other.ReceiveShadows
                && ShadowCastingMode == other.ShadowCastingMode;
        }

        public static bool operator ==(BatchKey a, BatchKey b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(BatchKey a, BatchKey b)
        {
            return !a.Equals(b);
        }
    }

    public unsafe class RenderMeshIndirectTest : MonoBehaviour
    {
        [Header("Rendering")]
        public Mesh[] Meshes;
        public Material[] Materials;
        public int MaxInstanceCount = 100;
        public ComputeShader CullingCS;
        public bool Draw = true;

        [Header("Other")]
        public uint Seed = 1234;

        Dictionary<Mesh, int> _meshToID = new Dictionary<Mesh, int>();
        Dictionary<int, Mesh> _idToMesh = new Dictionary<int, Mesh>();
        Dictionary<Material, int> _materialToID = new Dictionary<Material, int>();
        Dictionary<int, Material> _idToMaterial = new Dictionary<int, Material>();

        Dictionary<BatchKey, int> _batchKeyToInstanceCount = new Dictionary<BatchKey, int>();
        int _totalInstanceCount;
        int _totalBatchCount;

        GraphicsBuffer _cullingPlaneBuffer;
        GraphicsBuffer _descriptorBuffer;
        GraphicsBuffer _dataBuffer;
        GraphicsBuffer _batchOffsetBuffer;
        GraphicsBuffer _visibilityBuffer;
        GraphicsBuffer _indirectArgsBuffer;

        GraphicsBuffer.IndirectDrawIndexedArgs[] _indirectDrawIndexedArgs;

        CommandBuffer _cmd;

        MaterialPropertyBlock _mpb;

        int _cullingKernel;

        Unity.Mathematics.Random _random;

        int _buttonSize = 100;
        GUIStyle _style;

        static readonly int s_totalInstanceCountID = Shader.PropertyToID("totalInstanceCount");

        static readonly int s_cullingParameterID = Shader.PropertyToID("cullingParameter");
        static readonly int s_packedPlaneBufferID = Shader.PropertyToID("PackedPlaneBuffer");

        static readonly int s_descriptorBufferID = Shader.PropertyToID("DescriptorBuffer");
        static readonly int s_dataBufferID = Shader.PropertyToID("DataBuffer");
        static readonly int s_batchOffsetBufferID = Shader.PropertyToID("BatchOffsetBuffer");
        static readonly int s_visibilityBufferID = Shader.PropertyToID("VisibilityBuffer");
        static readonly int s_indirectArgsBufferID = Shader.PropertyToID("IndirectArgsBuffer");

        const int c_descriptorSize = sizeof(int) * 2;
        const int c_instanceSize = AABB.c_Size + PackedMatrix.c_Size;

        struct Descriptor
        {
            public int BatchID;
            public int DataOffset;
        }

        struct InstanceData
        {
            public AABB Bounds;
            public PackedMatrix Transform;
        }

        void Start()
        {
            _cmd = new CommandBuffer();
            _cmd.name = "GPUDriven";

            _cullingKernel = CullingCS.FindKernel("Culling");

            _random = new Unity.Mathematics.Random(Seed);

            Prepare();

            _style = new GUIStyle();
            _style.fontSize = 15;
            _style.normal.textColor = Color.white;
        }

        void OnDestroy()
        {
            _descriptorBuffer.Dispose();
            _dataBuffer.Dispose();
            _batchOffsetBuffer.Dispose();
            _visibilityBuffer.Dispose();
            _indirectArgsBuffer.Dispose();
        }

        void Prepare()
        {
            RegisterAssets();

            GenerateBatches();

            CreateBuffer();

            UploadData();

            GenerateCommand();

            GenerateIndirectDrawIndexedArgs();
        }

        void RegisterAssets()
        {
            for (int i = 0; i < Meshes.Length; ++i)
            {
                Mesh mesh = Meshes[i];
                _meshToID[mesh] = i;
                _idToMesh[i] = mesh;
            }

            for (int i = 0; i < Materials.Length; ++i)
            {
                Material material = Materials[i];
                _materialToID[material] = i;
                _idToMaterial[i] = material;
            }
        }

        void GenerateBatches()
        {
            _totalInstanceCount = 0;
            _totalBatchCount = 0;

            for (int iMesh = 0; iMesh < Meshes.Length; ++iMesh)
            {
                for (int iMaterial = 0; iMaterial < Materials.Length; ++iMaterial)
                {
                    Mesh mesh = Meshes[iMesh];
                    Material material = Materials[iMaterial];

                    int instanceCount = _random.NextInt(1, MaxInstanceCount + 1);

                    BatchKey key = new BatchKey();
                    key.MeshID = _meshToID[mesh];
                    key.SubmeshIndex = 0;
                    key.MaterialID = _materialToID[material];
                    key.FlipWinding = false;
                    key.Layer = 0;
                    key.ReceiveShadows = false;
                    key.ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    _batchKeyToInstanceCount.Add(key, instanceCount);
                    _totalInstanceCount += instanceCount;
                    _totalBatchCount++;
                }
            }

            Debug.Log($"Total instance count: {_totalInstanceCount}, total batch count: {_totalBatchCount}");
            int batchIndex = 0;
            foreach (var pair in _batchKeyToInstanceCount)
            {
                Debug.Log($"Batch: {batchIndex++}, instance count: {pair.Value}");
            }
        }

        void CreateBuffer()
        {
            _cullingPlaneBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, PlanePacket4.c_Size); // max 16 culling planes should be enough
            _descriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalInstanceCount, c_descriptorSize);
            _dataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, _totalInstanceCount, c_instanceSize);
            _batchOffsetBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalBatchCount, sizeof(int));
            _visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalInstanceCount, sizeof(int));
            _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, _totalBatchCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        }

        void UploadData()
        {
            NativeArray<Descriptor> descriptorArray = new NativeArray<Descriptor>(_totalInstanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<InstanceData> instanceDataArray = new NativeArray<InstanceData>(_totalInstanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> batchOffsetArray = new NativeArray<int>(_totalBatchCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            int instanceIndex = 0;
            int batchIndex = 0;

            foreach (var pair in _batchKeyToInstanceCount)
            {
                BatchKey key = pair.Key;
                int instanceCount = pair.Value;

                batchOffsetArray[batchIndex] = instanceIndex;

                for (int iInstance = 0; iInstance < instanceCount; ++iInstance)
                {
                    Descriptor descriptor = new Descriptor()
                    {
                        BatchID = batchIndex,
                        DataOffset = instanceIndex * c_instanceSize
                    };
                    descriptorArray[instanceIndex] = descriptor;

                    float4x4 matrix = float4x4.TRS(new float3(iInstance, batchIndex, 0) * 1.5f, quaternion.identity, new float3(1.0f, 1.0f, 1.0f));

                    InstanceData instanceData = new InstanceData();
                    instanceData.Transform = (PackedMatrix)matrix;

                    Mesh mesh = _idToMesh[key.MeshID];
                    AABB aabbLocal = new AABB(mesh.bounds);
                    instanceData.Bounds = AABB.Transform(matrix, aabbLocal);

                    instanceDataArray[instanceIndex] = instanceData;

                    instanceIndex++;
                }

                batchIndex++;
            }

            _descriptorBuffer.SetData(descriptorArray);
            _dataBuffer.SetData(instanceDataArray);
            _batchOffsetBuffer.SetData(batchOffsetArray);
        }

        void GenerateCommand()
        {
            _cmd.Clear();

            _cmd.SetComputeIntParam(CullingCS, s_totalInstanceCountID, _totalInstanceCount);
            _cmd.SetComputeBufferParam(CullingCS, _cullingKernel, s_descriptorBufferID, _descriptorBuffer);
            _cmd.SetComputeBufferParam(CullingCS, _cullingKernel, s_dataBufferID, _dataBuffer);
            _cmd.SetComputeBufferParam(CullingCS, _cullingKernel, s_batchOffsetBufferID, _batchOffsetBuffer);
            _cmd.SetComputeBufferParam(CullingCS, _cullingKernel, s_visibilityBufferID, _visibilityBuffer);
            _cmd.SetComputeBufferParam(CullingCS, _cullingKernel, s_indirectArgsBufferID, _indirectArgsBuffer);

            int threadGroupsX = (_totalInstanceCount + 63) / 64;
            _cmd.DispatchCompute(CullingCS, _cullingKernel, threadGroupsX, 1, 1);

            _mpb = new MaterialPropertyBlock();
            _mpb.SetBuffer(s_descriptorBufferID, _descriptorBuffer);
            _mpb.SetBuffer(s_dataBufferID, _dataBuffer);
            _mpb.SetBuffer(s_batchOffsetBufferID, _batchOffsetBuffer);
            _mpb.SetBuffer(s_visibilityBufferID, _visibilityBuffer);
        }

        void GenerateIndirectDrawIndexedArgs() 
        {
            _indirectDrawIndexedArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[_totalBatchCount];

            int batchIndex = 0;
            foreach (var pair in _batchKeyToInstanceCount)
            {
                BatchKey key = pair.Key;
                int instanceCount = pair.Value;

                Mesh mesh = _idToMesh[key.MeshID];

                _indirectDrawIndexedArgs[batchIndex].indexCountPerInstance = mesh.GetIndexCount(0);

                batchIndex++;
            }

            _indirectArgsBuffer.SetData(_indirectDrawIndexedArgs);
        }

        void UpdateCameraFrustumPlanes(Camera camera)
        {
            Plane[] planeArray = new Plane[6];
            GeometryUtility.CalculateFrustumPlanes(camera, planeArray);

            NativeArray<Plane> planes = new NativeArray<Plane>(6, Allocator.Temp);
            for (int i = 0; i < 6; ++i)
            {
                planes[i] = planeArray[i];
            }

            NativeArray<PlanePacket4> packedPlanes = CullingUtility.BuildSOAPlanePackets(planes, Allocator.Temp);

            int[] ints = new int[4];
            ints[0] = packedPlanes.Length;
            CullingCS.SetInts(s_cullingParameterID, ints);

            _cullingPlaneBuffer.SetData(packedPlanes);
            CullingCS.SetBuffer(_cullingKernel, s_packedPlaneBufferID, _cullingPlaneBuffer);
        }

        void Update()
        {
            if (Draw)
            {
                _indirectArgsBuffer.SetData(_indirectDrawIndexedArgs);

                UpdateCameraFrustumPlanes(Camera.main);

                Graphics.ExecuteCommandBuffer(_cmd);

                int batchIndex = 0;
                foreach (var pair in _batchKeyToInstanceCount)
                {
                    BatchKey key = pair.Key;
                    int instanceCount = pair.Value;

                    Mesh mesh = _idToMesh[key.MeshID];
                    Material material = _idToMaterial[key.MaterialID];

                    RenderParams rp = new RenderParams(material);
                    rp.worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one);
                    rp.matProps = _mpb;
                    
                    Graphics.RenderMeshIndirect(rp, mesh, _indirectArgsBuffer, 1, batchIndex);

                    batchIndex++;
                }
            }
        }

        void OnGUI()
        {
            if (GUILayout.Button("Test", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
            {
            }

            string log = "Info";
            GUILayout.Label(log, _style);
        }
    }
}