Shader "DynamicSpectrum"
{
    Properties
    {
        N ("N", Integer) = 256
        L ("L", Float) = 500.0
        t ("t", Float) = 0.0
        HStaticTex ("HStaticTex", 2D) = "" {}
    }

    SubShader
    {

        Pass
        {
            Name "DynamicSpectrum"

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            int N;
            uniform int OceanFFTTarget;
            float L;
            float t;
            sampler2D HStaticTex;

            static const float PI = 3.1415926535897932384626433832795;
            static const float g = 9.81;

            // Vertex shader function
            v2f_img vert (appdata_img v)
            {
                v2f_img o;
                // Transform vertex position from object space to clip space.
                // This is typically handled by Unity's internal matrices for fullscreen quads.
                o.pos = UnityObjectToClipPos(v.vertex);
                // Pass through the UV coordinates directly
                o.uv = v.texcoord;
                return o;
            }


            // MAYBE DON'T USE MRT IF WE'RE BLITTING

            // USE MRT
            /*
            struct FragOutput
            {
                float4 h_kt_x_y: SV_Target0;
                float4 h_kt_z_dzdz: SV_Target1;
                float4 h_kt_dxdx_dxdz: SV_Target2;
                float4 h_kt_dydx_dydz: SV_Target3;
            };
            */

            float2 complex_mult(float2 a, float2 b)
            {
                return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
            }

            float2 euler_formula(float theta)
            {
                return float2(cos(theta), sin(theta));
            }

            float4 frag(v2f_img IN) : SV_Target
            {
                // can add scalar lambda to horizontal displacements if you want

                int2 coords = IN.uv * N;
                float2 x = coords - float2(N, N) / 2.0;
                float2 k = 2.0 * PI * x / L;
                float k_norm = length(k);

                if (k_norm <= 0.0) { // note: used to say k_norm < 0.0001
                    return float4(0.0, 0.0, 0.0, 0.0);
                } else {
                    float temp_freq = sqrt(g * k_norm); // no quantization or shallow water for now
                    float4 h_static = tex2D(HStaticTex, IN.uv);
                    float2 h_kt = complex_mult(h_static.rg, euler_formula(temp_freq * t)) + complex_mult(h_static.ba, euler_formula(-temp_freq * t));

                    if (OceanFFTTarget == 0) {
                        float2 h_kt_x = complex_mult(float2(0.0, -k.x / k_norm), h_kt);
                        return float4(h_kt_x, h_kt);
                    }
                    if (OceanFFTTarget == 1) {
                        float2 h_kt_z = complex_mult(float2(0.0, -k.y / k_norm), h_kt);
                        float2 h_kt_dzdz = k.y * k.y / k_norm * h_kt;
                        return float4(h_kt_z, h_kt_dzdz);
                    }
                    if (OceanFFTTarget == 2) {
                        float2 h_kt_dxdx = k.x * k.x / k_norm * h_kt;
                        float2 h_kt_dxdz = k.x * k.y / k_norm * h_kt;
                        return float4(h_kt_dxdx, h_kt_dxdz);
                    }
                    else {
                        float2 h_kt_dydx = complex_mult(float2(0.0, k.x), h_kt);
                        float2 h_kt_dydz = complex_mult(float2(0.0, k.y), h_kt);
                        return float4(h_kt_dydx, h_kt_dydz);
                    }
                }
            }
            ENDCG
        }
    }
}
