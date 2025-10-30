using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine;
using System;

public class OceanRenderPass : ScriptableRenderPass
{

    private int N;
    private float L;
    public float t = 0.0f;
    private int num_stages;

    private Material butterfly_texture_material;
    private Material butterfly_compute_material;
    private Material static_spectrum_material;
    private Material dynamic_spectrum_material;
    private Material invert_permute_collate_material;
    private Material foam_material;

    private RenderTexture butterfly_texture;
    private RenderTexture h_static_texture;
    private RenderTexture ping;
    private RenderTexture pong;
    private RenderTexture temp_texture;
    public RenderTexture x_y_z_dzdz;
    public RenderTexture dxdx_dxdz_dydx_dydz;
    private Texture2D gauss_rand_texture;
    public RenderTexture foam;

    public OceanRenderPass(int N, float L, Shader butterfly_texture_shader, Shader static_spectrum_shader, Shader dynamic_spectrum_shader, Shader butterfly_compute_shader, Shader invert_permute_collate_shader, Shader foam_shader) {
        this.N = N;
        this.L = L;
        num_stages = (int)Math.Log(N, 2.0);

        butterfly_texture_material = new Material(butterfly_texture_shader);
        static_spectrum_material = new Material(static_spectrum_shader);
        dynamic_spectrum_material = new Material(dynamic_spectrum_shader);
        butterfly_compute_material = new Material(butterfly_compute_shader);
        invert_permute_collate_material = new Material(invert_permute_collate_shader);
        foam_material = new Material(foam_shader);

        initialize_textures();

        // Generate Butterfly Texture
        butterfly_texture_material.SetInteger("N", N);
        Graphics.Blit(null, butterfly_texture, butterfly_texture_material, -1); // SHOULD PROBABLY REPLACE THIS WITH URP BLITTER

        // Generate Gaussian Noise Texture
        gauss_rand_texture = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true);
        gauss_rand_texture.filterMode = FilterMode.Point;
        System.Random rand = new System.Random();
        for (int i=0; i<N; i++) {
            for (int j=0; j<N; j++) {
                gauss_rand_texture.SetPixel(i, j, new Color((float)box_muller(rand), (float)box_muller(rand), 0.0f, 1.0f));
            }
        }
        gauss_rand_texture.Apply(); // IS THERE A PROPER WAY TO DO THIS IN URP?

        // Set Shader Properties
        static_spectrum_material.SetInteger("N", N);
        static_spectrum_material.SetFloat("L", L);
        static_spectrum_material.SetTexture("GaussRandTex", gauss_rand_texture);
        dynamic_spectrum_material.SetInteger("N", N);
        dynamic_spectrum_material.SetFloat("L", L);
        dynamic_spectrum_material.SetTexture("HStaticTex", h_static_texture);
        butterfly_compute_material.SetInteger("N", N);
        butterfly_compute_material.SetTexture("ButterflyTex", butterfly_texture);
        invert_permute_collate_material.SetInteger("N", N);
        invert_permute_collate_material.SetTexture("FirstInput", temp_texture);
        foam_material.SetFloat("decay", 0.99f);
        foam_material.SetFloat("spread", 0.99f);
        foam_material.SetFloat("lambda_chop", -1.0f);
        foam_material.SetFloat("foam_threshold", 0.5f);
        foam_material.SetTexture("x_y_z_dzdz",  x_y_z_dzdz);
        foam_material.SetTexture("dxdx_dxdz_dydx_dydz", dxdx_dxdz_dydx_dydz);
    }

    public void update_static_spectrum(Vector2 wind, float scale, float spread_tightness) {
        static_spectrum_material.SetFloat("WindX", wind.x);
        static_spectrum_material.SetFloat("WindZ", wind.y);
        static_spectrum_material.SetFloat("Scale", scale);
        static_spectrum_material.SetFloat("SpreadTightness", spread_tightness);
        Graphics.Blit(null, h_static_texture, static_spectrum_material, -1); // SHOULD PROBABLY REPLACE THIS WITH URP BLITTER
    }

    private void initialize_textures() {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGBFloat);
        RenderTextureDescriptor butterfly_desc = new RenderTextureDescriptor(num_stages, N, RenderTextureFormat.ARGBFloat);
        butterfly_desc.sRGB = true;
        butterfly_texture = new RenderTexture(butterfly_desc);
        h_static_texture = new RenderTexture(desc);
        ping = new RenderTexture(desc);
        pong = new RenderTexture(desc);
        temp_texture = new RenderTexture(desc);
        x_y_z_dzdz = new RenderTexture(desc);
        dxdx_dxdz_dydx_dydz = new RenderTexture(desc);
        foam = new RenderTexture(desc);
        butterfly_texture.filterMode = FilterMode.Point;
        h_static_texture.filterMode = FilterMode.Point;
        ping.filterMode = FilterMode.Point;
        pong.filterMode = FilterMode.Point;
        temp_texture.filterMode = FilterMode.Point;
        x_y_z_dzdz.filterMode = FilterMode.Point;
        dxdx_dxdz_dydx_dydz.filterMode = FilterMode.Point;
        // important: use bilinear for foam
    }

    private double box_muller(System.Random rand) {
        double u1 = 1.0-rand.NextDouble(); //uniform(0,1] random doubles
        double u2 = 1.0-rand.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData rendering_data)
    {
        // Get a CommandBuffer from pool
        CommandBuffer cmd = CommandBufferPool.Get();

        // Add rendering commands to the CommandBuffer

        dynamic_spectrum_material.SetFloat("t", t);
        for (int target=0; target < 4; target++) {
            cmd.SetGlobalInteger("OceanFFTTarget", target); // We can maybe see if we can get MRT working after we test this and it works
            cmd.Blit(null, ping, dynamic_spectrum_material, -1);

            bool pingpong = true;
            
            for (int direction=0; direction < 2; direction++) {
                cmd.SetGlobalInteger("FFTDirection", direction);
                for (int stage=0; stage < num_stages; stage++) {
                    cmd.SetGlobalInteger("FFTStage", stage);
                    if (pingpong) {
                        cmd.Blit(ping, pong, butterfly_compute_material, -1);
                    } else {
                        cmd.Blit(pong, ping, butterfly_compute_material, -1);
                    }
                    pingpong = !pingpong;
                }
            }

            if (target % 2 == 0) {
                if (pingpong) {
                    cmd.CopyTexture(ping, temp_texture);
                } else {
                    cmd.CopyTexture(pong, temp_texture);
                }
            } else {
                RenderTexture output_tex = x_y_z_dzdz;
                if (target == 3) {
                    output_tex = dxdx_dxdz_dydx_dydz;
                }
                if (pingpong) {
                    cmd.Blit(ping, output_tex, invert_permute_collate_material, -1);
                } else {
                    cmd.Blit(pong, output_tex, invert_permute_collate_material, -1);
                }
            }
        }

        cmd.CopyTexture(foam, temp_texture);
        cmd.Blit(temp_texture, foam, foam_material, -1);


        // Execute the command buffer and release it back to the pool
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void release_textures() {
        butterfly_texture.Release();
        h_static_texture.Release();
        ping.Release();
        pong.Release();
        temp_texture.Release();
        x_y_z_dzdz.Release();
        dxdx_dxdz_dydx_dydz.Release();
        foam.Release();
    }
}
