using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.IndirectExample
{
    public class RenderMeshIndirectMergeTest : MonoBehaviour
    {
        [Header("Rendering")]
        public Mesh[] Meshes;
        public Material[] Materials;
        public int MaxInstanceCount = 100;
        public ComputeShader CullingCS;
        public bool Draw = true;

        [Header("Other")]
        public uint Seed = 1234;

        struct MeshInfo
        {
            public uint indexCountPerInstance;
            public uint startIndex;
            public uint baseVertexIndex;
        }

        struct VertexData
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector4 tangent;
            public Vector2 uv;

            public const int c_Size = (3 + 3 + 4 + 2) * 4;
        }

        Dictionary<Mesh, int> _meshToID = new Dictionary<Mesh, int>();
        Dictionary<int, Mesh> _idToMesh = new Dictionary<int, Mesh>();

        GraphicsBuffer IndexBuffer;
        GraphicsBuffer VertexBuffer;

        MeshInfo[] _meshInfos;

        Dictionary<Material, int> _materialToID = new Dictionary<Material, int>();
        Dictionary<int, Material> _idToMaterial = new Dictionary<int, Material>();

        //Dictionary<BatchKey, int> _batchKeyToInstanceCount = new Dictionary<BatchKey, int>();
        Dictionary<IndirectKey, List<CmdInfo>> _IndirectKeyToCmdInfoList = new Dictionary<IndirectKey, List<CmdInfo>>();
        int _totalInstanceCount;
        int _totalCmdCount;

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
        static readonly int s_IndirectArgsBufferID = Shader.PropertyToID("IndirectArgsBuffer");

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
            _cullingPlaneBuffer.Dispose();
        }

        void Prepare()
        {
            PrepareAssets();

            GenerateBatches();

            CreateBuffer();

            UploadData();

            GenerateCommand();

            GenerateIndirectDrawIndexedArgs();
        }

        void PrepareAssets()
        {
            for (int i = 0; i < Meshes.Length; ++i)
            {
                Mesh mesh = Meshes[i];
                _meshToID[mesh] = i;
                _idToMesh[i] = mesh;
            }

            MergeMesh();

            for (int i = 0; i < Materials.Length; ++i)
            {
                Material material = Materials[i];
                _materialToID[material] = i;
                _idToMaterial[i] = material;
            }
        }

        void MergeMesh()
        {
            int vertexCount = 0;
            int indexCount = 0;
            foreach (Mesh mesh in Meshes)
            {
                vertexCount += mesh.vertexCount;
                indexCount += (int)mesh.triangles.Length;
            }

            IndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indexCount, sizeof(int));
            VertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexCount, VertexData.c_Size);

            int currentVertexCount = 0;
            int currentIndexCount = 0;

            VertexData[] vertices = new VertexData[vertexCount];
            int[] indices = new int[indexCount];
            _meshInfos = new MeshInfo[Meshes.Length];

            for (int i = 0; i < Meshes.Length; i++)
            {
                Mesh mesh = Meshes[i];

                Array.Copy(mesh.triangles, 0, indices, currentIndexCount, mesh.triangles.Length);

                Vector3[] positions = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                Vector2[] uvs = mesh.uv;
                for (int j = 0; j < mesh.vertexCount; j++)
                {
                    vertices[currentVertexCount + j].position = positions[j];
                    vertices[currentVertexCount + j].normal = normals[j];
                    vertices[currentVertexCount + j].tangent = tangents[j];
                    vertices[currentVertexCount + j].uv = uvs[j];
                }

                _meshInfos[i] = new MeshInfo()
                {
                    indexCountPerInstance = (uint)mesh.triangles.Length,
                    startIndex = (uint)currentIndexCount,
                    baseVertexIndex = (uint)currentVertexCount,
                };

                currentVertexCount += mesh.vertexCount;
                currentIndexCount += (int)mesh.triangles.Length;
            }

            IndexBuffer.SetData(indices);
            VertexBuffer.SetData(vertices);
        }

        public struct IndirectKey : IEquatable<IndirectKey>
        {
            public int MaterialID;
            public byte Layer;
            public bool ReceiveShadows;
            public ShadowCastingMode ShadowCastingMode;

            public override int GetHashCode()
            {
                return MaterialID.GetHashCode()
                    ^ Layer.GetHashCode()
                    ^ ReceiveShadows.GetHashCode()
                    ^ ((int)(ShadowCastingMode)).GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is IndirectKey)
                    return Equals((IndirectKey)obj);

                return false;
            }

            public bool Equals(IndirectKey other)
            {
                return MaterialID == other.MaterialID
                    && Layer == other.Layer
                    && ReceiveShadows == other.ReceiveShadows
                    && ShadowCastingMode == other.ShadowCastingMode;
            }

            public static bool operator ==(IndirectKey a, IndirectKey b)
            {
                return a.Equals(b);
            }

            public static bool operator !=(IndirectKey a, IndirectKey b)
            {
                return !a.Equals(b);
            }
        }

        struct CmdInfo
        {
            public int MeshID;
            public int InstanceCount;
        }

        void GenerateBatches()
        {
            _totalInstanceCount = 0;
            _totalCmdCount = 0;

            for (int iMaterial = 0; iMaterial < Materials.Length; ++iMaterial)
            {
                IndirectKey indirectKey = new IndirectKey()
                {
                    MaterialID = iMaterial,
                    Layer = 0,
                    ReceiveShadows = false,
                    ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off
                };

                if (!_IndirectKeyToCmdInfoList.TryGetValue(indirectKey, out var list))
                {
                    list = new List<CmdInfo>();
                    _IndirectKeyToCmdInfoList.Add(indirectKey, list);
                }

                for (int iMesh = 0; iMesh < Meshes.Length; ++iMesh)
                {
                    CmdInfo cmdInfo = new CmdInfo()
                    {
                        MeshID = iMesh,
                        InstanceCount = _random.NextInt(1, MaxInstanceCount + 1)
                    };

                    list.Add(cmdInfo);

                    _totalInstanceCount += cmdInfo.InstanceCount;
                    _totalCmdCount++;
                }
            }
        }

        void CreateBuffer()
        {
            _cullingPlaneBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, PlanePacket4.c_Size); // max 16 culling planes should be enough
            _descriptorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalInstanceCount, c_descriptorSize);
            _dataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, _totalInstanceCount, c_instanceSize);
            _batchOffsetBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalCmdCount, sizeof(int));
            _visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _totalInstanceCount, sizeof(int));
            _indirectArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, _totalCmdCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        }

        void UploadData()
        {
            NativeArray<Descriptor> descriptorArray = new NativeArray<Descriptor>(_totalInstanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<InstanceData> instanceDataArray = new NativeArray<InstanceData>(_totalInstanceCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> batchOffsetArray = new NativeArray<int>(_totalCmdCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            int instanceIndex = 0;
            int batchIndex = 0;

            foreach (var pair in _IndirectKeyToCmdInfoList)
            {
                IndirectKey key = pair.Key;
                List<CmdInfo> cmdInfoList = pair.Value;

                foreach(var cmdInfo in cmdInfoList)
                {
                    batchOffsetArray[batchIndex] = instanceIndex;

                    for (int iInstance = 0; iInstance < cmdInfo.InstanceCount; ++iInstance)
                    {
                        Descriptor descriptor = new Descriptor()
                        {
                            BatchID = batchIndex,
                            DataOffset = instanceIndex * c_instanceSize
                        };
                        descriptorArray[instanceIndex] = descriptor;

                        float4x4 matrix = float4x4.TRS(new float3(iInstance, batchIndex, 0) * 2f, quaternion.identity, new float3(1.0f, 1.0f, 1.0f));

                        InstanceData instanceData = new InstanceData();
                        instanceData.Transform = (PackedMatrix)matrix;

                        Mesh mesh = _idToMesh[cmdInfo.MeshID];
                        AABB aabbLocal = new AABB(mesh.bounds);
                        instanceData.Bounds = AABB.Transform(matrix, aabbLocal);

                        instanceDataArray[instanceIndex] = instanceData;

                        instanceIndex++;
                    }

                    batchIndex++;
                }
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
            _cmd.SetComputeBufferParam(CullingCS, _cullingKernel, s_IndirectArgsBufferID, _indirectArgsBuffer);

            int threadGroupsX = (_totalInstanceCount + 63) / 64;
            _cmd.DispatchCompute(CullingCS, _cullingKernel, threadGroupsX, 1, 1);

            _mpb = new MaterialPropertyBlock();
            _mpb.SetBuffer(s_descriptorBufferID, _descriptorBuffer);
            _mpb.SetBuffer(s_dataBufferID, _dataBuffer);
            _mpb.SetBuffer(s_batchOffsetBufferID, _batchOffsetBuffer);
            _mpb.SetBuffer(s_visibilityBufferID, _visibilityBuffer);

            _mpb.SetBuffer("IndirectIndexBuffer", IndexBuffer);
            _mpb.SetBuffer("IndirectVertexBuffer", VertexBuffer);
        }

        void GenerateIndirectDrawIndexedArgs()
        {
            _indirectDrawIndexedArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[_totalCmdCount];

            int cmdIndex = 0;
            foreach (var pair in _IndirectKeyToCmdInfoList)
            {
                IndirectKey key = pair.Key;
                List<CmdInfo> cmdInfoList = pair.Value;

                foreach (var cmdInfo in cmdInfoList)
                {
                    var meshInfo = _meshInfos[cmdInfo.MeshID];

                    _indirectDrawIndexedArgs[cmdIndex].indexCountPerInstance = meshInfo.indexCountPerInstance;
                    _indirectDrawIndexedArgs[cmdIndex].startIndex = meshInfo.startIndex;
                    _indirectDrawIndexedArgs[cmdIndex].baseVertexIndex = meshInfo.baseVertexIndex;

                    cmdIndex++;
                }
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

                int startCommand = 0;
                foreach (var pair in _IndirectKeyToCmdInfoList)
                {
                    IndirectKey key = pair.Key;
                    Material material = _idToMaterial[key.MaterialID];

                    List<CmdInfo> cmdInfoList = pair.Value;

                    RenderParams rp = new RenderParams(material);
                    rp.worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one);
                    rp.matProps = _mpb;

                    Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, IndexBuffer, _indirectArgsBuffer, cmdInfoList.Count, startCommand);

                    startCommand += cmdInfoList.Count;
                }
            }
        }

        void OnGUI()
        {
            if (GUILayout.Button("Test", GUILayout.Width(_buttonSize), GUILayout.Height(_buttonSize)))
            {
            }

            string log = "todo";
            GUILayout.Label(log, _style);
        }
    }
}