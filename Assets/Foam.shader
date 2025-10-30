Shader "Foam"
{
    Properties
    {
        decay ("decay", Float) = 0.99
        spread ("spread", Float) = 0.99
        lambda_chop ("lambda_chop", Float) = -1.0
        foam_threshold ("foam_threshold", Float) = 0.3
        _MainTex ("Input Tex", 2D) = "" {}
        x_y_z_dzdz ("x_y_z_dzdz", 2D) = "" {}
        dxdx_dxdz_dydx_dydz ("dxdx_dxdz_dydx_dydz", 2D) = "" {}
    }

    SubShader
    {

        Pass
        {
            Name "Foam"

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            float decay;
            float spread;
            float lambda_chop;
            float foam_threshold;
            sampler2D _MainTex;
            sampler2D x_y_z_dzdz;
            sampler2D dxdx_dxdz_dydx_dydz;


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
                float4 data_1 = tex2D(x_y_z_dzdz, IN.uv);
                float4 data_2 = tex2D(dxdx_dxdz_dydx_dydz, IN.uv);
                // take samples at nearby places from THESE and average rather than doing it with the old foam
                // try gaussian

                float J_xx = 1 + lambda_chop * data_2.x;
                float J_zz = 1 + lambda_chop * data_1.w;
                float J_xz = lambda_chop * data_2.y;

                float det = J_xx * J_zz - J_xz * J_xz;
                float uv_offset = 0.00390625;
                float old_foam = tex2D(_MainTex, IN.uv).x;/* * spread
                                    + (tex2D(_MainTex, IN.uv + float2(0.0, uv_offset))
                                    + tex2D(_MainTex, IN.uv + float2(0.0, -uv_offset))
                                    + tex2D(_MainTex, IN.uv + float2(uv_offset, 0.0))
                                    + tex2D(_MainTex, IN.uv + float2(-uv_offset, 0.0))
                                    + tex2D(_MainTex, IN.uv + float2(uv_offset, uv_offset))
                                    + tex2D(_MainTex, IN.uv + float2(uv_offset, -uv_offset))
                                    + tex2D(_MainTex, IN.uv + float2(-uv_offset, uv_offset))
                                    + tex2D(_MainTex, IN.uv + float2(-uv_offset, -uv_offset))) * (1.0 - spread) / 8.0;*/
                // The whole spreading thing sucks. We should find another way to make it look un-pixelated
                float new_foam = saturate(det / -foam_threshold + 1.0);
                
                return saturate(old_foam * decay + new_foam);

                // potentially later use other channels for eigenvectors and stuff
            }
            ENDCG
        }
    }
}
