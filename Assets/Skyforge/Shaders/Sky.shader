// The sky: a photographed panorama turned so its sun sits where the scene's sun light is, and
// melted into the fog colour at the horizon so the sea and the far shore have nothing to end against.
Shader "Skyforge/Sky"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "grey" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Float) = 1
        _Rotation ("Rotation (radians)", Float) = 0
        _HorizonColor ("Horizon Colour", Color) = (0.7, 0.8, 0.9, 1)
        _HorizonHeight ("Horizon Blend Height", Float) = 0.10
        _HorizonAmount ("Horizon Blend Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _HorizonColor;
                float _Exposure;
                float _Rotation;
                float _HorizonHeight;
                float _HorizonAmount;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(float4 positionOS : POSITION)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(positionOS.xyz);
                output.direction = positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float s = sin(_Rotation), c = cos(_Rotation);
                float2 turned = float2(d.x * c - d.z * s, d.x * s + d.z * c);
                float u = atan2(turned.x, turned.y) * (0.5 / PI) + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5;
                // Sampled at mip 0: the jump in u behind the viewer would otherwise draw a seam.
                half3 color = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, float2(u, v), 0).rgb;
                color *= _Tint.rgb * _Exposure;

                float t = saturate((_HorizonHeight - d.y) / (_HorizonHeight + 0.03));
                t = t * t * (3.0 - 2.0 * t);
                // Below the horizon there is only haze, whatever the photograph had there.
                float amount = d.y < -0.03 ? 1.0 : t * _HorizonAmount;
                color = lerp(color, _HorizonColor.rgb, amount);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
