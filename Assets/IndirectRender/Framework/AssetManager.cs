using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public class AssetManager
    {
        MeshMerger _meshMerger;
        MaterialMerger _materialMerger;

        IDGenerator _meshIDGenerator;
        Dictionary<Mesh, int> _meshToID = new Dictionary<Mesh, int>();
        Dictionary<int, Mesh> _idToMesh = new Dictionary<int, Mesh>();
        Dictionary<int, MeshInfo> _idToMeshInfo = new Dictionary<int, MeshInfo>();

        IDGenerator _materialIDGenerator;

        Dictionary<Material, int> _materialToID = new Dictionary<Material, int>();
        Dictionary<int, Material> _idToMaterial = new Dictionary<int, Material>();
        Dictionary<int, ShaderLayout> _idToShaderLayout = new Dictionary<int, ShaderLayout>();
        NativeParallelHashMap<int, BatchMaterialID> _idToBatchMaterialID;

        BatchRendererGroup _brg;

        public void Init(MeshMerger meshMerger, MaterialMerger materialMerger, BatchRendererGroup brg)
        {
            _meshMerger = meshMerger;
            _materialMerger = materialMerger;
            _brg = brg;

            _meshIDGenerator = new IDGenerator();
            _meshIDGenerator.Init(256);

            _materialIDGenerator = new IDGenerator();
            _materialIDGenerator.Init(256);

            _meshIDGenerator.GetID(); // 0 is reserved for invalid mesh
            _materialIDGenerator.GetID(); // 0 is reserved for invalid material

            _idToBatchMaterialID = new NativeParallelHashMap<int, BatchMaterialID>(16, Allocator.Persistent);
        }

        public void Dispose()
        {
            _idToBatchMaterialID.Dispose();

            _meshIDGenerator.Dispose();
            _materialIDGenerator.Dispose();
        }

        public int RegisterMesh(Mesh mesh)
        {
            if (_meshToID.TryGetValue(mesh, out int id))
            {
                return id;
            }
            else
            {
                MeshInfo meshInfo = _meshMerger.Merge(mesh);
                if (meshInfo.IsValid)
                {
                    int newID = _meshIDGenerator.GetID();
                    _meshToID[mesh] = newID;
                    _idToMesh[newID] = mesh;
                    _idToMeshInfo.Add(newID, meshInfo);

                    return newID;
                }
                else
                {
                    return -1;
                }
            }
        }

        public void UnregisterMesh(int id)
        {
            if (_idToMesh.TryGetValue(id, out var mesh))
            {
                MeshInfo meshInfo = _idToMeshInfo[id];
                _meshMerger.Release(meshInfo);

                _meshIDGenerator.ReturnID(id);
                _meshToID.Remove(mesh);
                _idToMesh.Remove(id);
                _idToMeshInfo.Remove(id);
            }
        }

        public int RegisterMaterial(Material material, bool merge)
        {
            if (_materialToID.TryGetValue(material, out int id))
            {
                return id;
            }
            else
            {
                int newID = _materialIDGenerator.GetID();

                if (merge)
                {
                    // when merge the material, the original material does not need to enable the keyword ZGAME_INDIRECT

                    MaterialMergeInfo mmi = _materialMerger.Merge(material);


                }
                else
                {
                    if (!material.IsKeywordEnabled("ZGAME_INDIRECT"))
                    {
                        Utility.LogError($"material({material.name}) must enable keyword ZGAME_INDIRECT");
                        return -1;
                    }
                }

                

                
                _materialToID[material] = newID;
                _idToMaterial[newID] = material;
                _idToBatchMaterialID[newID] = _brg.RegisterMaterial(material);

                return newID;
            }
        }

        public void UnregisterMaterial(int id)
        {
            if (_idToMaterial.TryGetValue(id, out var material))
            {
                _materialIDGenerator.ReturnID(id);
                _brg.UnregisterMaterial(_idToBatchMaterialID[id]);
                _idToMaterial.Remove(id);
                _materialToID.Remove(material);
                _idToBatchMaterialID.Remove(id);
            }

            if (_idToShaderLayout.ContainsKey(id))
            {
                _idToShaderLayout.Remove(id);
            }
        }

        public SubMeshInfo GetMeshInfo(int meshID, int subMeshIndex)
        {
            if (_idToMeshInfo.TryGetValue(meshID, out var meshInfo))
            {
                return meshInfo.SubMeshInfos[subMeshIndex];
            }
            else
            {
                Utility.LogError($"MeshInfo not found, id={meshID}");
                return SubMeshInfo.s_Invalid;
            }
        }

        public Material GetMaterial(int materialID)
        {
            if (_idToMaterial.TryGetValue(materialID, out Material material))
            {
                return material;
            }
            else
            {
                Utility.LogError($"Material not found, id={materialID}");
                return null;
            }
        }

        public BatchMaterialID GetBatchMaterialID(int materialID)
        {
            if (_idToBatchMaterialID.TryGetValue(materialID, out BatchMaterialID batchMaterialID))
            {
                return batchMaterialID;
            }
            else
            {
                Utility.LogError($"BatchMaterialID not found, id={materialID}");
                return BatchMaterialID.Null;
            }
        }

        // todo:
        public NativeParallelHashMap<int, BatchMaterialID> GetIdToBatchMaterialID()
        {
            return _idToBatchMaterialID;
        }

        public void AddShaderLayout(int materialID, ShaderLayout shaderLayout)
        {
            if (!_idToShaderLayout.ContainsKey(materialID))
            {
                _idToShaderLayout.Add(materialID, shaderLayout);
            }
        }

        public ShaderLayout GetShaderLayout(int materialID)
        {
            if (_idToShaderLayout.TryGetValue(materialID, out var shaderLayout))
            {
                return shaderLayout;
            }
            else
            {
                Utility.LogError($"ShaderLayout not found, id={materialID}");
                return ShaderLayout.s_Invalid;
            }
        }
    }
}