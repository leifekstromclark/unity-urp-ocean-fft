Shader "ButterflyCompute"
{
    Properties
    {
        N ("N", Integer) = 256
        _MainTex ("Input Tex", 2D) = "" {}
        ButterflyTex ("ButterflyTex", 2D) = "" {}
    }

    SubShader
    {

        Pass
        {
            Name "ButterflyCompute"

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            int N;
            uniform int FFTStage;
            uniform int FFTDirection;
            sampler2D _MainTex;
            sampler2D ButterflyTex;

            static const float PI = 3.1415926535897932384626433832795;

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

            float2 complex_mult(float2 a, float2 b)
            {
                return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
            }

            float4 frag(v2f_img IN) : SV_Target
            {
                float4 butterfly_data;
                float4 p_data;
                float4 q_data;
                float num_stages = log2(N);

                if (FFTDirection) {
                    // Vertical butterflies
                    butterfly_data = tex2D(ButterflyTex, float2(1.0 / num_stages * (FFTStage + 0.5), IN.uv.y));
                    p_data = tex2D(_MainTex, float2(IN.uv.x, 1.0 / float(N) * (butterfly_data.b + 0.5)));
                    q_data = tex2D(_MainTex, float2(IN.uv.x, 1.0 / float(N) * (butterfly_data.a + 0.5)));
                } else {
                    // Horizontal butterflies
                    butterfly_data = tex2D(ButterflyTex, float2(1.0 / num_stages * (FFTStage + 0.5), IN.uv.x));
                    p_data = tex2D(_MainTex, float2(1.0 / float(N) * (butterfly_data.b + 0.5), IN.uv.y));
                    q_data = tex2D(_MainTex, float2(1.0 / float(N) * (butterfly_data.a + 0.5), IN.uv.y));
                }

                float2 w = butterfly_data.rg;
                float2 H = p_data.rg + complex_mult(w, q_data.rg);
                float2 H_chan_two = p_data.ba + complex_mult(w, q_data.ba);

                return float4(H, H_chan_two);
            }
            ENDCG
        }
    }
}
