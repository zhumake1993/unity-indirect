Shader"GPU Driven/IndirectShader0"
{
    Properties
    {
        // istance property, todo: remove it
        _IndirectPeoperty0("Color", Color) = (0,0,0,0)
        
        // material property
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
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
                UNITY_DEFINE_INSTANCED_PROP(half, _Metallic)
            CBUFFER_END

            #if ZGAME_INDIRECT
                #define _IndirectPeoperty0 ZGmae_Indirect_Get_Float4(0)
            #else
                #define _IndirectPeoperty0 float4(0,0,0,0)
            #endif

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
                float4 color : COLOR;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                #if ZGAME_INDIRECT
                
                ZGmae_Indirect_Setup(input.svVertexID, input.svInstanceID);

                IndirectVertexData indirectVertexData = ZGame_Indirect_Get_IndirectVertexData();
                
                input.positionOS = indirectVertexData.position;

                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                output.color = _IndirectPeoperty0;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                return input.color * _Metallic;
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

			CBUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(half, _Metallic)
            CBUFFER_END

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

                output.positionCS.x += _Metallic * 0.0001f;

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