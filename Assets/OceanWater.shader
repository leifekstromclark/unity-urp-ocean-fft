Shader "OceanWater"
{

    // Rewrite other shaders in URP DX11 Style:

    // tex and sampler style

    // use TransformObjectToHClip

    // don't use UnityCG.cginc. Define your own Attributes and Varyings.

    // CBUFFER_START thing

    // we might have to use the URP functions in our command buffers after all . . .







    // this shader itself could maybe use gpu instancing


    // can get the eigenvectors to add cool directional effects to the foam

    // swell could also be fun (could add a few extra sins, a new spectrum, or do texture scrolling)

    Properties
    {
        // See if it works to remove some of these from the properties section
        reflection_tex ("Reflection Skybox HDR", 2D) = "" {}
        reflection_tint ("Reflection Skybox Tint", Color) = (.5, .5, .5, .5)
        reflection_exposure ("Reflection Skybox Exposure", Range(0, 8)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define M_PI 3.1415926535897932384626433832795

            // Wave-related properties
            int N;
            float L;
            float lambda_chop;
            float foam_threshold;
            TEXTURE2D(x_y_z_dzdz);
            TEXTURE2D(dxdx_dxdz_dydx_dydz);
            SamplerState sampler_linear_repeat;
            TEXTURE2D(reflection_tex);
            SAMPLER(sampler_reflection_tex);
            TEXTURE2D(foam_tex);
            SAMPLER(sampler_foam_tex);

            // Reflection-related properties
            half4 reflection_tex_HDR;
            half4 reflection_tint;
            half reflection_exposure;
            
            // LOD-related properties
            int LOD_depth;
            float LOD_ranges[100]; // SHADER_BUFFER_MAX_LOD = 100 in ocean controller
            float LOD_levels[1023]; // INSTANCE_BATCH_SIZE = 1023 in ocean controller
            float masks[1023]; // INSANCE_BATCH_SIZE = 1023 in ocean controller
            float morph_area;
            float min_LOD_cell_size;
            int mesh_res;
            float4 view_position;

            struct Attributes
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                // The positions in this struct must have the SV_POSITION semantic.
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float2 solve_quadratic(float a, float b, float c) {
                float descriminant = b * b - 4 * a * c;
                if (descriminant < 0) {
                    return float2(0.0, 0.0);
                }
                float sqrt_descriminant = sqrt(descriminant);
                return float2(-b + sqrt(descriminant), -b - sqrt(descriminant)) / 2.0 / a;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);


                float3 pos_ws = TransformObjectToWorld(IN.position.xyz);
                #ifdef INSTANCING_ON
                if (LOD_levels[IN.instanceID] < LOD_depth) {
                    float morph_k = saturate((length(pos_ws - view_position.xyz) / LOD_ranges[LOD_levels[IN.instanceID]] - (1.0 - morph_area)) / morph_area);
                    float quad_size = min_LOD_cell_size * pow(2.0, LOD_levels[IN.instanceID]) / float(mesh_res);
                    float2 morph = float2(uint(IN.uv.x * mesh_res) % 2, uint(IN.uv.y * mesh_res) % 2) * quad_size * morph_k;
                    pos_ws = pos_ws + float3(morph.x, 0.0, morph.y);
                }
                #endif


                // Setting lambda chop to -1.0 is a bandaid solution of making this negative will work for now but I should really figure out why the displacements are reverse . . .
                float epsilon = 0.1;

                float2 tex_coords = pos_ws.xz / L;

                float4 data_1 = SAMPLE_TEXTURE2D_LOD(x_y_z_dzdz, sampler_linear_repeat, tex_coords, 0);
                float4 data_2 = SAMPLE_TEXTURE2D_LOD(dxdx_dxdz_dydx_dydz, sampler_linear_repeat, tex_coords, 0);

                float J_xx = 1 + lambda_chop * data_2.x;
                float J_zz = 1 + lambda_chop * data_1.w;
                float J_xz = lambda_chop * data_2.y;

                float det = J_xx * J_zz - J_xz * J_xz;

                float new_lambda = lambda_chop;

                // eliminate any folding from the mesh - this currently DOES NOT WORK
                /*
                if (det < epsilon) {
                    // There should always be two roots I think
                    float2 roots = solve_quadratic(data_2.x * data_1.w - data_2.y * data_2.y, data_2.x + data_1.w, 1.0);

                    float smooth_lambda = roots.x;
                    if (abs(lambda_chop - roots.y) < abs(lambda_chop - roots.x)) {
                        smooth_lambda = roots.y;
                    }
                    new_lambda = smooth_lambda + (lambda_chop - smooth_lambda) * saturate(det / epsilon); 
                }
                */

                pos_ws += float3(new_lambda * data_1.x, data_1.y, new_lambda * data_1.z);
                float3 tangent = float3(1.0 + new_lambda * data_2.x, data_2.z, new_lambda * data_2.y);
                float3 binormal = float3(new_lambda * data_2.y, data_2.w, 1.0 + new_lambda * data_1.w);

                OUT.normalWS = normalize(cross(binormal, tangent));
                OUT.positionWS = pos_ws;
                OUT.positionHCS = TransformWorldToHClip(pos_ws);
                OUT.uv = IN.uv;
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                return OUT;
            }


            float2 ToRadialCoords(float3 coords)
            {
                float3 normalizedCoords = normalize(coords);
                float latitude = acos(normalizedCoords.y);
                float longitude = atan2(normalizedCoords.z, normalizedCoords.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5/M_PI, 1.0/M_PI);
                return float2(0.5,1.0) - sphereCoords;
            }

            half3 DecodeHDR(half4 data, half4 decodeInstructions)
            {
                half alpha = decodeInstructions.w * (data.a - 1.0) + 1.0;

                #if defined(UNITY_COLORSPACE_GAMMA)
                    return (decodeInstructions.x * alpha) * data.rgb;
                #else
                    return (decodeInstructions.x * pow(alpha, decodeInstructions.y)) * data.rgb;
                #endif
            }

            half4 sample_skybox(float3 direction) {
                float2 tex_coords = ToRadialCoords(direction);
                half4 tex = SAMPLE_TEXTURE2D(reflection_tex, sampler_reflection_tex, tex_coords);
                half3 c = DecodeHDR(tex, reflection_tex_HDR);
                c = c * reflection_tint.rgb * half3(2.0, 2.0, 2.0);
                c *= reflection_exposure;
                return half4(c, 1.0);
            }

            float schlick_fresnel(float outer_index, float inner_index, float theta) {
                float R_0 = pow((outer_index - inner_index) / (outer_index + inner_index), 2);
                return saturate(R_0 + (1 - R_0) * pow(1 - cos(theta), 5));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // ADD CLIPPING OUTSIDE VIEW DISTANCE?
                
                #ifdef INSTANCING_ON
                uint mask = masks[IN.instanceID];
                if (mask % 2 && IN.uv.x < 0.5 && IN.uv.y < 0.5) discard;
                mask /= 2;
                if (mask % 2 && IN.uv.x > 0.5 && IN.uv.y < 0.5) discard;
                mask /= 2;
                if (mask % 2 && IN.uv.x < 0.5 && IN.uv.y > 0.5) discard;
                mask /= 2;
                if (mask % 2 && IN.uv.x > 0.5 && IN.uv.y > 0.5) discard; // THIS CAUSES FUCKASS SEAMS CUZ OF MSAA
                #endif

                half4 water_color = half4(0.0, 0.1, 0.2, 1.0);
                half4 foam_color = half4(1.0, 1.0, 1.0, 1.0);
                half4 specular_color = half4(1.0, 1.0, 0.0, 1.0);
                float3 light_dir = float3(0.0, 1.0, 0.0);
                float3 view_dir = normalize(_WorldSpaceCameraPos + IN.positionWS);
                float3 normal = normalize(IN.normalWS);
                float3 reflect_dir = -light_dir + 2 * dot(light_dir, normal) * normal;
                float shininess = 5.0;

                float diffuse = saturate(dot(normal, light_dir));
                float specular = saturate(pow(dot(view_dir, reflect_dir), shininess));


                float fresnel = schlick_fresnel(1.0, 1.33, acos(dot(view_dir, normal)));

                half4 reflect_color = sample_skybox(reflect_dir);
                float foam = 0.0; //SAMPLE_TEXTURE2D(foam_tex, sampler_foam_tex, IN.uv).x;
                float4 diffuse_color = water_color * (1.0 - foam) + foam_color * foam;
                fresnel *= 1.0 - foam;



                return diffuse_color * diffuse; //+ fresnel * (reflect_color + specular_color * specular);
            }
            ENDHLSL
        }
    }
}