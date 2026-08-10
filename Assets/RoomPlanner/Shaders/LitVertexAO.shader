// Minimal lit surface shader with per-vertex AO (vertex color .r) — docs/design/04.
// Our meshes are procedural, so real GI cannot bake; the builders bake corner/skirting
// occlusion into vertex colors instead, and this shader multiplies it into the ambient
// term. Deliberately simple: Lambert + main-light shadows + SH ambient, matte.
// Must be correct for Quest single-pass instanced stereo (URP/Unity 6).
Shader "RoomPlanner/LitVertexAO"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cull("Cull", Float) = 2
        // Floors carry TWO UV sets: uv0 = blueprint-plan projection (the plan aligns
        // across slabs), uv1 = metric world XZ (finish textures tile in metres).
        // Selectable flips this per-renderer via MPB when a texture finish is applied.
        _UseUV1("Sample UV1 (metric channel)", Float) = 0
        // Finish gloss (design/04 v1.2): 0 = the old pure-matte look, 1 = glossy tile.
        _Smoothness("Smoothness", Range(0, 1)) = 0
        // Optional relief (design/22, baked laminate): our procedural meshes carry no
        // tangents, the TBN comes from screen-space derivatives in the fragment.
        // MPB-driven (keywords can't ride a property block), so a float flag, not a
        // shader_feature; the uniform branch is coherent and free when off.
        _BumpMap("Normal Map", 2D) = "bump" {}
        _HasBump("Has Normal Map", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // Shared per-material data. Kept in HLSLINCLUDE so every pass sees the exact
        // same UnityPerMaterial layout (SRP batcher requires consistent layouts).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _Cull;
            float _UseUV1;
            float _Smoothness;
            float _HasBump;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            // On Quest (XR mobile) URP disables the generic _SHADOWS_SOFT keyword and
            // enables a static per-quality keyword instead (ShadowUtils.
            // SetSoftShadowQualityShaderKeywords + PlatformAutoDetect.isXRMobile), so
            // all four variants must be declared or shadows silently turn hard on device.
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            // Tangent-free normal mapping (Schüler's cotangent frame): reconstruct the
            // T/B axes from screen-space derivatives of position and UV. Our meshes are
            // procedural and deliberately carry no tangent stream (design/22).
            half3 PerturbNormal(half3 n, float3 positionWS, float2 uv, half3 nTS)
            {
                float3 dpdx = ddx(positionWS), dpdy = ddy(positionWS);
                float2 duvdx = ddx(uv), duvdy = ddy(uv);
                float3 r1 = cross(dpdy, float3(n)), r2 = cross(float3(n), dpdx);
                float3 t = r1 * duvdx.x + r2 * duvdy.x;
                float3 b = r1 * duvdx.y + r2 * duvdy.y;
                float invMax = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-12));
                return normalize(nTS.x * half3(t * invMax) + nTS.y * half3(b * invMax) + nTS.z * n);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;   // metric channel (floors); 0 when absent
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   ao         : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(lerp(v.uv, v.uv1, saturate(_UseUV1)), _BaseMap);
                o.ao = v.color.r;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half3 n = normalize(i.normalWS);

                // optional relief (design/22): laminate/brick finishes set _BumpMap +
                // _HasBump via MPB; everything else keeps the geometric normal
                if (_HasBump > 0.5)
                {
                    half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                    n = PerturbNormal(n, i.positionWS, i.uv, nTS);
                }

                // ambient (SH) scaled by baked vertex AO
                half3 color = SampleSH(n) * albedo * i.ao;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                // positionWS overload applies shadow-distance fade (MainLightShadow);
                // the shadowCoord-only overload does not and clamps at the map edge.
                Light mainLight = GetMainLight(shadowCoord, i.positionWS, half4(1, 1, 1, 1));
                half ndl = saturate(dot(n, mainLight.direction));
                color += albedo * mainLight.color
                    * (ndl * mainLight.shadowAttenuation * mainLight.distanceAttenuation);

                // finish gloss (design/04 v1.2): one cheap Blinn-Phong lobe off the main
                // light; _Smoothness = 0 keeps the original pure-Lambert look
                if (_Smoothness > 0.001)
                {
                    half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half3 halfway = normalize(mainLight.direction + viewDir);
                    half specPow = exp2(1.0h + half(_Smoothness) * 9.0h);
                    half spec = pow(saturate(dot(n, halfway)), specPow) * half(_Smoothness);
                    color += mainLight.color
                        * (spec * ndl * mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                }

                #if defined(_ADDITIONAL_LIGHTS)
                // Forward path only: uses per-object light indices, which Forward+ does
                // not populate. If the renderer ever switches to Forward+, this loop must
                // move to LIGHT_LOOP_BEGIN/END with the _FORWARD_PLUS keyword.
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    color += albedo * l.color
                        * (saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation);
                }
                #endif

                return half4(color, 1);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                float3 lightDir = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                // URP 17 clamps to the near plane for directional lights only.
                o.positionCS = ApplyShadowClamping(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            // SSAO's DepthNormals prepass only sees objects that carry this pass —
            // without it our walls/floors were MISSING from the AO's depth+normal
            // buffers, so the occlusion floated free of the building and showed
            // through storeys (device 2026-08-11).
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(normalize(i.normalWS), 0.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
