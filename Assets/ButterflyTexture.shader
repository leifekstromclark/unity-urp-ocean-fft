Shader "ButterflyTexture"
{
    Properties
    {
        N ("N", Integer) = 256
    }

    SubShader
    {

        Pass
        {
            Name "ButterflyTexture"

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            int N;

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

            int bit_reverse(int n)
            {
                int r = 0;
                int l = log2(N);
                for (int i = 0; i < l; i++) {
                    r *= 2;
                    r += n % 2;
                    n /= 2;
                }
                return r;
            }


            float4 frag(v2f_img IN) : SV_Target
            {
                int2 x = int2(IN.uv.x * log2(N), IN.uv.y * N);

                float k = (x.y * (float(N) / pow(2, x.x + 1))) % N;
                float2 twiddle = float2(cos(2.0 * PI * k / float(N)), sin(2.0 * PI * k / float(N)));

                int butterfly_span = pow(2, x.x);

                int butterfly_wing = 0;

                if (x.y % pow(2, x.x + 1) < pow(2, x.x))
                {
                   butterfly_wing = 1;
                }

                if (x.x == 0) {
                    if (butterfly_wing == 1) {
                        return float4(twiddle.x, twiddle.y, bit_reverse(x.y), bit_reverse(x.y + 1));
                    } else {
                        return float4(twiddle.x, twiddle.y, bit_reverse(x.y - 1), bit_reverse(x.y));
                    }
                }
                else {
                    if (butterfly_wing == 1) {
                        return float4(twiddle.x, twiddle.y, x.y, x.y + butterfly_span);
                    } else {
                        return float4(twiddle.x, twiddle.y, x.y - butterfly_span, x.y);
                    }
                }
            }
            ENDCG
        }
    }
}
