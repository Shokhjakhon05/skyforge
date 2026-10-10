// Every solid thing in the world: terrain, buildings, vehicles, drones. The colour comes from
// the base map (a palette strip or the painted terrain); a detail texture projected from three
// sides gives it grain, a bent normal and a roughness, so metal, concrete and soil stop looking
// like the same plastic. Meshes carry no tangents and no second UV set: none are needed.
Shader "Skyforge/Surface"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Detail ("Detail (normal x, normal y, light, roughness)", 2D) = "gray" {}
        _DetailSteep ("Detail on slopes", 2D) = "gray" {}
        _Tiling ("Detail repeats per metre", Float) = 0.3
        _SteepBlend ("Slope detail amount", Range(0, 1)) = 0
        _Macro ("Large-scale variation", Range(0, 1)) = 0
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _DetailStrength ("Detail light strength", Range(0, 1)) = 1
        _NormalStrength ("Detail normal strength", Range(0, 2)) = 1
        _Emission ("Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Detail); SAMPLER(sampler_Detail);
        TEXTURE2D(_DetailSteep); SAMPLER(sampler_DetailSteep);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _Emission;
            float _Tiling;
            half _SteepBlend;
            half _Macro;
            half _Metallic;
            half _Smoothness;
            half _DetailStrength;
            half _NormalStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ SKYFORGE_FLAT SKYFORGE_LITE
            #pragma shader_feature_local _ _TERRAIN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 detailPosition : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                float fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalOS = input.normalOS;
                output.uv = input.uv;
                // Projected in object space so the grain travels with a moving vehicle, and scaled
                // by the object's size so a model shrunk to fit keeps the same grain as its neighbour.
                float3 scale = float3(
                    length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                    length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)),
                    length(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22)));
                output.detailPosition = input.positionOS.xyz * scale * _Tiling;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 normalWS = normalize(input.normalWS);
                half light = 1.0;
                half roughness = 0.5;

            #ifndef SKYFORGE_FLAT
                float3 n = normalize(input.normalOS);
                float3 weight = abs(n);
                weight = weight * weight * weight * weight;
                weight /= (weight.x + weight.y + weight.z);
                float3 p = input.detailPosition;

            #ifdef SKYFORGE_LITE
                // The middle tier looks at the detail once, from whichever side the face mostly shows.
                bool alongX = weight.x > max(weight.y, weight.z);
                bool alongY = !alongX && weight.y >= weight.z;
                float2 uv = alongX ? p.zy : (alongY ? p.xz : p.xy);
                half4 detail = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, uv);
                light = lerp(1.0, detail.b * 2.0, _DetailStrength);
                roughness = detail.a;
                float2 tilt = detail.rg * 2.0 - 1.0;
                float3 bend = alongX ? float3(0.0, tilt.y, tilt.x) : (alongY ? float3(tilt.x, 0.0, tilt.y) : float3(tilt.x, tilt.y, 0.0));
            #else
                half4 sideX = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, p.zy);
                half4 top = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, p.xz);
                half4 sideZ = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, p.xy);
            #ifdef _TERRAIN
                // Rock shows through wherever the ground tilts, long before it is a cliff.
                half steep = _SteepBlend * smoothstep(0.10, 0.32, 1.0 - n.y);
                top = lerp(top, SAMPLE_TEXTURE2D(_DetailSteep, sampler_DetailSteep, p.xz), steep);
                sideX = lerp(sideX, SAMPLE_TEXTURE2D(_DetailSteep, sampler_DetailSteep, p.zy), _SteepBlend);
                sideZ = lerp(sideZ, SAMPLE_TEXTURE2D(_DetailSteep, sampler_DetailSteep, p.xy), _SteepBlend);
            #endif
                half4 detail = sideX * weight.x + top * weight.y + sideZ * weight.z;

                light = lerp(1.0, detail.b * 2.0, _DetailStrength);
            #ifdef _TERRAIN
                // One more look at the same picture, far larger, breaks up the repeat seen from the air.
                half macro = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, p.xz * 0.11 + 0.37).b;
                light *= lerp(1.0, 0.55 + macro * 0.9, _Macro);
            #endif
                roughness = detail.a;

                float3 bendX = float3(0.0, sideX.g * 2.0 - 1.0, sideX.r * 2.0 - 1.0);
                float3 bendY = float3(top.r * 2.0 - 1.0, 0.0, top.g * 2.0 - 1.0);
                float3 bendZ = float3(sideZ.r * 2.0 - 1.0, sideZ.g * 2.0 - 1.0, 0.0);
                float3 bend = bendX * weight.x + bendY * weight.y + bendZ * weight.z;
            #endif
                normalWS = normalize(TransformObjectToWorldNormal(normalize(n + bend * _NormalStrength)));
            #endif

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = base.rgb * light;
                surface.metallic = _Metallic;
                surface.smoothness = saturate(_Smoothness * 2.0 * (1.0 - roughness));
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                surface.emission = _Emission.rgb;
                surface.normalTS = half3(0.0, 0.0, 1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
