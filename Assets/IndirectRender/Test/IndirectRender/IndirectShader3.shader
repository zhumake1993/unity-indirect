Shader"GPU Driven/IndirectShader2"
{
    Properties
    {
        // istance property, todo: remove it
        _IndirectPeoperty0("Color0", Color) = (0,0,0,0)
        
        // material property
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _BaseMap_ST("Till And Offset",Vector) = (1,1,0,0)
        
        [MainTexture] _NormalMap("Normal", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode"="UniversalForward"
            }

            Cull Back

            HLSLPROGRAM
            
            #pragma target 4.5
            
            #pragma vertex vert
            #pragma fragment frag

            #pragma editor_sync_compilation
		    #pragma enable_d3d11_debug_symbols

            #pragma multi_compile _ ZGAME_INDIRECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseMap_ST)
                UNITY_DEFINE_INSTANCED_PROP(half, _Metallic)
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            #if ZGAME_INDIRECT
                #define _IndirectPeoperty0 ZGmae_Indirect_Get_Float4(0)
            #else
                #define _IndirectPeoperty0 float4(0,0,0,0)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;

                #if ZGAME_INDIRECT
                uint svVertexID : SV_VertexID;
                uint svInstanceID : SV_InstanceID;
                #endif
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                #if ZGAME_INDIRECT
                
                ZGmae_Indirect_Setup(input.svVertexID, input.svInstanceID);

                IndirectVertexData indirectVertexData = ZGame_Indirect_Get_IndirectVertexData();
                
                input.positionOS = indirectVertexData.position;
                input.uv = indirectVertexData.uv0;

                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                #if ZGAME_INDIRECT
                output.color = _IndirectPeoperty0;
                #else
                output.color = _IndirectPeoperty0;
                #endif

                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 result = input.color;

                float4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                result *= albedoAlpha;

                float4 normal = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                result *= normal;
                
                return result * _Metallic;
            }
            
            ENDHLSL
        }
        
        Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest Always
			ColorMask 0
			Cull Off

			HLSLPROGRAM
			
            #pragma target 4.5
            
            #pragma vertex vert
            #pragma fragment frag

            #pragma editor_sync_compilation
		    #pragma enable_d3d11_debug_symbols

            #pragma multi_compile _ ZGAME_INDIRECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;

                #if ZGAME_INDIRECT
                uint svVertexID : SV_VertexID;
                uint svInstanceID : SV_InstanceID;
                #endif
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
			
            Varyings vert(Attributes input)
            {
                Varyings output;

                #if ZGAME_INDIRECT
                
                ZGmae_Indirect_Setup(input.svVertexID, input.svInstanceID);

                IndirectVertexData indirectVertexData = ZGame_Indirect_Get_IndirectVertexData();
                
                input.positionOS = indirectVertexData.position;

                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                return half4(1.0f, 1.0f, 1.0f, 1.0f);
            }

			ENDHLSL
		}
    }
}