Shader "InvertPermuteCollate"
{
    Properties
    {
        N ("N", Integer) = 256
        FirstInput ("FirstInput", 2D) = "" {}
        _MainTex ("SecondInput", 2D) = "" {}
    }

    SubShader
    {

        Pass
        {
            Name "InvertPermuteCollate"

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            int N;
            sampler2D FirstInput;
            sampler2D _MainTex;

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
                
                float perm = 1.0 - 2.0 * ((coords.x + coords.y) % 2);

                float4 final = perm * float4(tex2D(FirstInput, IN.uv).rb, tex2D(_MainTex, IN.uv).rb) / float(N * N);
                return final;
            }
            ENDCG
        }
    }
}
