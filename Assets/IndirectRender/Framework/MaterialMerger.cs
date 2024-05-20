using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZGame.Indirect
{
    public struct MaterialMergeKey
    {
        public int ShaderNameHash;
        public UnsafeList<int> KeywordHashes;
    }

    public struct MaterialPropertyDefination
    {
        public float Metallic;
        public float3 Pad;
    }

    public struct MaterialMergeInfo
    {
        
    }

    public class MaterialMerger
    {
        public void Init()
        { 
        }

        public void Dispose()
        { 
        }

        public MaterialMergeInfo Merge(Material material)
        {
            Debug(material);

            return new MaterialMergeInfo();
        }

        void Debug(Material material)
        {
            LocalKeyword[] localKeywords = material.enabledKeywords;
            string log = $"material({material.name}) keywords:\n";
            foreach (var keyword in localKeywords)
                log += $"\t{keyword.name}\n";

            log += $"material({material.name}) float properties:\n";

            string[] floatProps = material.GetPropertyNames(MaterialPropertyType.Float);
            foreach (var prop in floatProps)
                log += $"\tFloat:{prop}\n";

            string[] intProps = material.GetPropertyNames(MaterialPropertyType.Int);
            foreach (var prop in intProps)
                log += $"\tInt:{prop}\n";

            string[] vectorProps = material.GetPropertyNames(MaterialPropertyType.Vector);
            foreach (var prop in vectorProps)
                log += $"\tVector:{prop}\n";

            string[] matrixProps = material.GetPropertyNames(MaterialPropertyType.Matrix);
            foreach (var prop in matrixProps)
                log += $"\t{prop}\n";

            string[] textureProps = material.GetPropertyNames(MaterialPropertyType.Texture);
            foreach (var prop in textureProps)
                log += $"\tTexture:{prop}\n";

            string[] constantProps = material.GetPropertyNames(MaterialPropertyType.ConstantBuffer);
            foreach (var prop in constantProps)
                log += $"\tConstantBuffer:{prop}\n";

            string[] computerProps = material.GetPropertyNames(MaterialPropertyType.ComputeBuffer);
            foreach (var prop in computerProps)
                log += $"\tComputeBuffer:{prop}\n";

            UnityEngine.Debug.Log(log); // 
        }
    }
}