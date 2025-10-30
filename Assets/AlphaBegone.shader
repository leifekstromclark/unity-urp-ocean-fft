Shader "AlphaBegone"
{
    Properties
    {
        _MainTex ("Input Tex", 2D) = "" {}
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
                return float4(tex2D(_MainTex, IN.uv).rgb, 1.0);
            }
            ENDCG
        }
    }
}
