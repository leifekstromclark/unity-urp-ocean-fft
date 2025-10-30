using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Rendering;

public class OceanControllerPrototype : MonoBehaviour
{
    // If we implement getting shaders by name then we will make these private

    public Material ButterflyComputeMaterial;
    public Material ButterflyTextureMaterial;
    public Material StaticSpectrumMaterial;
    public Material DynamicSpectrumMaterial;
    public Material InvertPermuteCollateMaterial;

    public Material AlphaBegoneMaterial; // for testing


    public RenderTexture ButterflyTexture;
    public RenderTexture HStaticTexture;
    public RenderTexture Ping;
    public RenderTexture Pong;
    public RenderTexture TempTex;
    public RenderTexture x_y_z_dzdz;
    public RenderTexture dxdx_dxdz_dydx_dydz;
    public Texture2D GaussRandTex;


    public int N = 256;
    public float t = 0.0f;
    public float L = 5.0f;

    private int num_stages;


    // Start is called before the first frame update
    void Start()
    {
        num_stages = (int)Math.Log(N, 2.0);
        
        InitializeTextures();

        // Generate Butterfly Texture
        ButterflyTextureMaterial.SetInteger("N", N);
        Graphics.Blit(null, ButterflyTexture, ButterflyTextureMaterial, -1);

        // Generate Gaussian Noise Texture
        GaussRandTex = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true);
        GaussRandTex.filterMode = FilterMode.Point;
        System.Random rand = new System.Random();
        for (int i=0; i<N; i++) {
            for (int j=0; j<N; j++) {
                GaussRandTex.SetPixel(i, j, new Color((float)BoxMuller(rand), (float)BoxMuller(rand), 0.0f, 1.0f));
            }
        }
        GaussRandTex.Apply();

        // Generate HStatic Texture
        StaticSpectrumMaterial.SetInteger("N", N);
        StaticSpectrumMaterial.SetFloat("L", L);
        StaticSpectrumMaterial.SetFloat("WindX", 31.0f);
        StaticSpectrumMaterial.SetFloat("WindZ", 0.0f);
        StaticSpectrumMaterial.SetFloat("Scale", 1.0f);
        StaticSpectrumMaterial.SetFloat("SpreadTightness", 2.0f);
        StaticSpectrumMaterial.SetTexture("GaussRandTex", GaussRandTex);
        Graphics.Blit(null, HStaticTexture, StaticSpectrumMaterial, -1);

        // Set propeties for Dynamic Spectrum, ButterflyCompute, and InvertPermuteCollate
        DynamicSpectrumMaterial.SetInteger("N", N);
        DynamicSpectrumMaterial.SetFloat("L", L);
        DynamicSpectrumMaterial.SetTexture("HStaticTex", HStaticTexture);
        ButterflyComputeMaterial.SetInteger("N", N);
        ButterflyComputeMaterial.SetTexture("ButterflyTex", ButterflyTexture);
        InvertPermuteCollateMaterial.SetInteger("N", N);
        InvertPermuteCollateMaterial.SetTexture("FirstInput", TempTex);

        // For testing
        GetComponent<UnityEngine.UI.RawImage>().texture = Pong;
        

        /*
        Material testing_mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        testing_mat.SetTexture("_BaseMap", ButterflyTexture);
        GetComponent<Renderer>().material = testing_mat;
        */
    }

    // Update is called once per frame
    void Update()
    {
        
        t += Time.deltaTime;

        DynamicSpectrumMaterial.SetFloat("t", t);
        CommandBuffer cmd = CommandBufferPool.Get();
        for (int target=0; target < 4; target++) {
            cmd.SetGlobalInteger("OceanFFTTarget", target); // We can maybe see if we can get MRT working after we test this and it works
            cmd.Blit(null, Ping, DynamicSpectrumMaterial, -1);

            bool pingpong = true;
            
            for (int direction=0; direction < 2; direction++) {
                cmd.SetGlobalInteger("FFTDirection", direction);
                for (int stage=0; stage < num_stages; stage++) {
                    cmd.SetGlobalInteger("FFTStage", stage);
                    if (pingpong) {
                        cmd.Blit(Ping, Pong, ButterflyComputeMaterial, -1);
                    } else {
                        cmd.Blit(Pong, Ping, ButterflyComputeMaterial, -1);
                    }
                    pingpong = !pingpong;
                }
            }

            if (target % 2 == 0) {
                if (pingpong) {
                    cmd.Blit(Ping, TempTex);
                } else {
                    cmd.Blit(Pong, TempTex);
                }
            } else {
                RenderTexture output_tex = x_y_z_dzdz;
                if (target == 3) {
                    output_tex = dxdx_dxdz_dydx_dydz;
                }
                if (pingpong) {
                    cmd.Blit(Ping, output_tex, InvertPermuteCollateMaterial, -1);
                } else {
                    cmd.Blit(Pong, output_tex, InvertPermuteCollateMaterial, -1);
                }
            }
        }
        cmd.Blit(x_y_z_dzdz, Pong, AlphaBegoneMaterial, -1);

        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }


    void InitializeTextures() {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(N, N, RenderTextureFormat.ARGBFloat);
        RenderTextureDescriptor butterfly_desc = new RenderTextureDescriptor(num_stages, N, RenderTextureFormat.ARGBFloat);
        butterfly_desc.sRGB = true;
        ButterflyTexture = new RenderTexture(butterfly_desc);
        HStaticTexture = new RenderTexture(desc);
        Ping = new RenderTexture(desc);
        Pong = new RenderTexture(desc);
        TempTex = new RenderTexture(desc);
        x_y_z_dzdz = new RenderTexture(desc);
        dxdx_dxdz_dydx_dydz = new RenderTexture(desc);
        ButterflyTexture.filterMode = FilterMode.Point;
        HStaticTexture.filterMode = FilterMode.Point;
        Ping.filterMode = FilterMode.Point;
        Pong.filterMode = FilterMode.Point;
        TempTex.filterMode = FilterMode.Point;
        x_y_z_dzdz.filterMode = FilterMode.Point;
        dxdx_dxdz_dydx_dydz.filterMode = FilterMode.Point;
    }

    double BoxMuller(System.Random rand) {
        double u1 = 1.0-rand.NextDouble(); //uniform(0,1] random doubles
        double u2 = 1.0-rand.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
    }
}
