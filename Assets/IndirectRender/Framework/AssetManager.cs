using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ZGame.Indirect
{
    public class AssetManager
    {
        MeshMerger _meshMerger;

        IDGenerator _meshIDGenerator;
        Dictionary<MeshKey, int> _meshKeyToID = new Dictionary<MeshKey, int>();
        Dictionary<int, MeshKey> _idToMeshKey = new Dictionary<int, MeshKey>();
        Dictionary<int, MeshInfo> _idToMeshInfo = new Dictionary<int, MeshInfo>();

        IDGenerator _materialIDGenerator;
        Dictionary<Material, int> _materialToID = new Dictionary<Material, int>();
        Dictionary<int, Material> _idToMaterial = new Dictionary<int, Material>();
        Dictionary<int, ShaderLayout> _idToShaderLayout = new Dictionary<int, ShaderLayout>();

        public void Init(MeshMerger meshMerger)
        {
            _meshMerger = meshMerger;

            _meshIDGenerator = new IDGenerator();
            _meshIDGenerator.Init(Utility.c_MeshIDInitialCapacity);

            _materialIDGenerator = new IDGenerator();
            _materialIDGenerator.Init(Utility.c_MaterialIDInitialCapacity);
        }

        public void Dispose()
        {
            _meshIDGenerator.Dispose();
            _materialIDGenerator.Dispose();
        }

        public int RegisterMesh(MeshKey meshKey)
        {
            if (_meshKeyToID.TryGetValue(meshKey, out int id))
            {
                return id;
            }
            else
            {
                MeshInfo meshInfo = _meshMerger.Merge(meshKey);
                if (meshInfo.IsValid)
                {
                    int newID = _meshIDGenerator.GetID();
                    _meshKeyToID[meshKey] = newID;
                    _idToMeshKey[newID] = meshKey;
                    _idToMeshInfo.Add(newID, meshInfo);

                    return newID;
                }
                else
                {
                    return -1;
                }
            }
        }

        public int RegisterMaterial(Material material)
        {
            if (_materialToID.TryGetValue(material, out int id))
            {
                return id;
            }
            else
            {
                if (!material.IsKeywordEnabled("ZGAME_INDIRECT"))
                {
                    Utility.LogError($"material({material.name}) must enable keyword ZGAME_INDIRECT");
                    return -1;
                }

                int newID = _materialIDGenerator.GetID();
                _materialToID[material] = newID;
                _idToMaterial[newID] = material;

                return newID;
            }
        }

        public MeshInfo GetMeshInfo(int meshID)
        {
            if (_idToMeshInfo.TryGetValue(meshID, out var meshInfo))
            {
                return meshInfo;
            }
            else
            {
                Utility.LogError($"MeshInfo not found, id={meshID}");
                return MeshInfo.s_Invalid;
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