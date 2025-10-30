Shader "StaticSpectrum"
{
    Properties
    {
        N ("N", Integer) = 256
        L ("L", Float) = 500.0
        Scale ("Scale", Float) = 1.0
        WindX ("WindX", Float) = 15.0
        WindZ ("WindZ", Float) = 0.0
        SpreadTightness ("SpreadTightness", Float) = 2.0
        GaussRandTex ("GaussRandTex", 2D) = "" {}
    }

    SubShader
    {

        Pass
        {
            Name "StaticSpectrum"

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            int N;
            float L;
            float Scale;
            float WindX;
            float WindZ;
            float SpreadTightness;
            sampler2D GaussRandTex;

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


            float4 frag(v2f_img IN) : SV_Target
            {
                int2 coords = IN.uv * N;
                float2 x = coords - float2(N, N) / 2.0;
                float2 k = 2.0 * PI * x / L;
                float k_sq = dot(k, k);
                // note: used to say k_sq < 0.00000001
                if (k_sq <= 0.0 || coords.x == 0 || coords.y == 0) { // ZERO OUT THE ROW AND COLUMN FOR WHICH -k IS NOT DEFINED
                    return float4(0.0, 0.0, 0.0, 0.0);
                } else {
                    float2 wind = float2(WindX, WindZ);
                    float L_phillips = dot(wind, wind) / g;
                    float P_no_dir = Scale * exp(-1 / k_sq / L_phillips / L_phillips) / k_sq / k_sq;

                    // still havent quite figured out the convergence thing

                    // seems to look fine without zeroing half

                    float2 wind_dir = normalize(wind);

                    float2 gauss_rand = tex2D(GaussRandTex, IN.uv).rg;

                    float2 opposite_uv = (0.5 - x + float2(N, N) / 2.0) / float(N); // Remember, the middle of our FFT is not the middle of the texture
                    float2 gauss_rand_neg = tex2D(GaussRandTex, opposite_uv).rg;

                    // increase power on dot to tighten directional spread
                    float cos_k_wind = dot(normalize(k), wind_dir);
                    float2 h_k = gauss_rand * sqrt(P_no_dir * pow(abs(cos_k_wind), SpreadTightness) / 2.0);
                    float cos_negk_wind = dot(normalize(-k), wind_dir);
                    float2 h_negk = gauss_rand_neg * sqrt(P_no_dir * pow(abs(cos_negk_wind), SpreadTightness) / 2.0);

                    // if you want the waves really just going in one direction you can do this
                    /*
                    if (cos_k_wind < 0.0) {
                        h_k = float2(0.0, 0.0);
                    }
                    if (cos_negk_wind < 0.0) {
                        h_negk = float2(0.0, 0.0);
                    }
                    */

                    // only storing h_k and obtaining h_negk ffrom opposite_uv in dynamicspectrum shader

                    // gpgpu paper also had a clamp
                    return float4(h_k.x, h_k.y, h_negk.x, -h_negk.y); // remember conjugate! // stuff seems weird and symetrical when conjugate used . . . idk if i like it
                }
            }
            ENDCG
        }
    }
}
