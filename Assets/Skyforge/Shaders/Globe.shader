// The world map globe: a painted sphere lit from a fixed direction, with a thin glow of
// atmosphere at the rim. It ignores the scene's sun and fog, which belong to the islands.
Shader "Skyforge/Globe"
{
    Properties
    {
        _BaseMap ("Surface", 2D) = "white" {}
        _RimColor ("Atmosphere", Color) = (0.35, 0.60, 1.0, 1)
        _LightDir ("Light Direction", Vector) = (-0.45, 0.45, -0.75, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _RimColor;
                float4 _LightDir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float3 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;

                float lit = saturate(dot(normal, normalize(_LightDir.xyz)));
                float3 color = surface * (0.22 + 0.95 * lit);

                float rim = pow(1.0 - saturate(dot(normal, viewDir)), 3.0);
                color += _RimColor.rgb * rim * (0.35 + 0.65 * lit);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
