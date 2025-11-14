using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;




public class OceanController : MonoBehaviour
{
    // OceanFFT-related fields
    [SerializeField] private int N = 256;
    [SerializeField] private int num_cascades = 3;
    [SerializeField] private float L_0 = 500.0f;
    [SerializeField] private Vector2 wind = new Vector2(15.0f, 0.0f);
    [SerializeField] private float scale = 1.0f;
    [SerializeField] private float spread_tightness = 2.0f;

    private OceanRenderPass render_pass;


    // Shaders
    [SerializeField] private Shader butterfly_texture_shader;
    [SerializeField] private Shader static_spectrum_shader;
    [SerializeField] private Shader dynamic_spectrum_shader;
    [SerializeField] private Shader butterfly_compute_shader;
    [SerializeField] private Shader invert_permute_collate_shader;
    [SerializeField] private Shader foam_shader;
    [SerializeField] private Material ocean_water_material;


    // LOD-related fields
    [SerializeField] private float max_view_distance = 500.0f;
    [SerializeField] private float LOD_range_multiplier = 10.0f; // Must be greater than 2. - ALSO NEED TO CONSIDER MORPH AREAS THIS LOWER BOUND COULD BE GREATER
    [SerializeField] private int mesh_instance_resolution = 16;
    [SerializeField] private float min_LOD_cell_size = 1.0f;
    [SerializeField] private float morph_area = 0.2f;
    private int LOD_depth;
    private const int SHADER_BUFFER_MAX_LOD = 100;
    private const int INSTANCE_BATCH_SIZE = 1023;
    private enum MASK_FLAGS {
        NONE = 0,
        TL = 1,
        TR = 1 << 1,
        BL = 1 << 2,
        BR = 1 << 3,
        ALL = TL | TR | BL | BR
    };
    private float[] LOD_ranges;
    private List<Matrix4x4[]> instance_transforms;
    private List<RenderParams> instance_render_params;
    private Mesh mesh;

    private void OnEnable()
    {
        // Calculate LOD ranges
        LOD_depth = Mathf.Clamp((int)Mathf.Ceil(Mathf.Log(max_view_distance / min_LOD_cell_size / LOD_range_multiplier) / Mathf.Log(2.0f)), 0, SHADER_BUFFER_MAX_LOD);
        LOD_ranges = new float[LOD_depth+1];
        for (int i=0; i<LOD_depth; i++) {
            LOD_ranges[i] = (LOD_range_multiplier * min_LOD_cell_size * Mathf.Pow(2.0f, i));
        }
        LOD_ranges[LOD_depth] = max_view_distance;

        instance_transforms = new List<Matrix4x4[]>();
        instance_render_params = new List<RenderParams>();

        // placeholder view position
        calculate_LOD_instances(new Vector3(0.0f, 30.0f, 0.0f));


        // prepare cascades
        for (int i=0; i < num_cascades; i++) {
            float L = L_0 * Mathf.Pow(N / 2, i); // im not confident in this . . . recheck it. remember one of the rows/columns is zeroed
            // ALSO I think maybe the invert shader should just divide by (N - 1)^2 rather than N^2
            // use these parameters to set up cascades
        }

        render_pass = new OceanRenderPass(N, L_0, butterfly_texture_shader, static_spectrum_shader, dynamic_spectrum_shader, butterfly_compute_shader, invert_permute_collate_shader, foam_shader);
        render_pass.update_static_spectrum(wind, scale, spread_tightness);
        RenderPipelineManager.beginContextRendering += on_begin_context;


        generate_mesh();

        ocean_water_material.SetFloatArray("LOD_ranges", LOD_ranges);
        ocean_water_material.SetInteger("LOD_depth", LOD_depth);
        ocean_water_material.SetFloat("morph_area", morph_area);
        ocean_water_material.SetInteger("mesh_res", mesh_instance_resolution);
        ocean_water_material.SetFloat("min_LOD_cell_size", min_LOD_cell_size);

        ocean_water_material.SetInteger("N", 256);
        ocean_water_material.SetFloat("L", L_0);
        ocean_water_material.SetFloat("lambda_chop", -1.0f);
        ocean_water_material.SetFloat("foam_threshold", 0.3f);
        ocean_water_material.SetTexture("x_y_z_dzdz", render_pass.x_y_z_dzdz);
        ocean_water_material.SetTexture("dxdx_dxdz_dydx_dydz", render_pass.dxdx_dxdz_dydx_dydz);
        ocean_water_material.SetTexture("foam_tex", render_pass.foam);
    }

    private void Update() {
        for (int i=0; i<instance_transforms.Count; i++) { // Use the transform count rather than the RenderParams count because the latter could be too long
            Graphics.RenderMeshInstanced(instance_render_params[i], mesh, 0, instance_transforms[i]);
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= on_begin_context;
        render_pass.release_textures();
    }

    private void on_begin_context(ScriptableRenderContext context, List<Camera> cameras)
    {
        render_pass.t += Time.deltaTime;

        // This is the best way I can think of to do this.
        if (cameras is not null && cameras.Count > 0 && cameras[0].GetComponent<UniversalAdditionalCameraData>() is not null) {
            cameras[0].GetComponent<UniversalAdditionalCameraData>().scriptableRenderer.EnqueuePass(render_pass);
        }
    }


    public void generate_mesh() {
        Vector3[] vertices = new Vector3[(mesh_instance_resolution+1)*(mesh_instance_resolution+1)];
        Vector2[] uv = new Vector2[(mesh_instance_resolution+1)*(mesh_instance_resolution+1)];
        int[] triangles = new int[mesh_instance_resolution*mesh_instance_resolution*6];

        for (int z=0; z<=mesh_instance_resolution; z++) {
            for (int x=0; x<=mesh_instance_resolution; x++) {
                int vert_ind = z*(mesh_instance_resolution+1)+x;
                vertices[vert_ind] = new Vector3(x, 0.0f, z) / (float)mesh_instance_resolution;
                uv[vert_ind] = new Vector2(x, z) / (float)mesh_instance_resolution;
                if (z < mesh_instance_resolution && x < mesh_instance_resolution) {
                    int quad_ind = (z*mesh_instance_resolution+x)*6;
                    triangles[quad_ind] = vert_ind;
                    triangles[quad_ind+1] = vert_ind+mesh_instance_resolution+2;
                    triangles[quad_ind+2] = vert_ind+1;
                    triangles[quad_ind+3] = vert_ind;
                    triangles[quad_ind+4] = vert_ind+mesh_instance_resolution+1;
                    triangles[quad_ind+5] = vert_ind+mesh_instance_resolution+2;
                }
            }
        }

        mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
    }

    public void calculate_LOD_instances(Vector3 view_position) { // everything should be done in worldspace but we should add functionality for a global y offset
        ocean_water_material.SetVector("view_position", new Vector4(view_position.x, view_position.y, view_position.z, 1.0f));


        List<Matrix4x4> transforms_acc = new List<Matrix4x4>();
        List<float> LOD_levels_acc = new List<float>();
        List<float> masks_acc = new List<float>();

        // identify the grid of relevant cells around the view position and traverse their quadtrees
        float max_LOD_cell_size = min_LOD_cell_size * Mathf.Pow(2.0f, LOD_depth);
        int low_cell_x = (int)(Mathf.Floor((view_position.x - max_view_distance) / max_LOD_cell_size));
        int low_cell_z = (int)(Mathf.Floor((view_position.z - max_view_distance) / max_LOD_cell_size));
        int high_cell_x = (int)(Mathf.Floor((view_position.x + max_view_distance) / max_LOD_cell_size));
        int high_cell_z = (int)(Mathf.Floor((view_position.z + max_view_distance) / max_LOD_cell_size));
        for (int i=low_cell_z; i<=high_cell_z; i++) {
            for (int j=low_cell_x; j<=high_cell_x; j++) {
                quadtree_traverse(new Vector2(j, i) * max_LOD_cell_size, LOD_depth, view_position, transforms_acc, LOD_levels_acc, masks_acc);
            }
        }

        // Prepare data for instance batches
        instance_transforms.Clear();

        int rp_ind = 0;
        int batch_start = 0;
        while (batch_start < transforms_acc.Count) {
            int clipped_batch_size = Mathf.Min(transforms_acc.Count - batch_start, INSTANCE_BATCH_SIZE);
            instance_transforms.Add(transforms_acc.GetRange(batch_start, clipped_batch_size).ToArray());

            // Add a new element to the list if we hit the end
            if (rp_ind >= instance_render_params.Count) {
                RenderParams rp = new RenderParams(ocean_water_material);
                rp.matProps = new MaterialPropertyBlock();
                instance_render_params.Add(rp);
            }
            // We want to reuse material property blocks in possible
            instance_render_params[rp_ind].matProps.SetFloatArray("LOD_levels", LOD_levels_acc.GetRange(batch_start, clipped_batch_size).ToArray());
            //instance_render_params[rp_ind].matProps.SetFloatArray("masks", masks_acc.GetRange(batch_start, clipped_batch_size).ToArray());

            rp_ind++;
            batch_start = rp_ind * INSTANCE_BATCH_SIZE;
        }
    }

    private bool circle_intersect_AArect(Rect rect, Vector2 c_center, float c_radius) {
        if ((c_center - rect.min).magnitude <= c_radius ) {
            return true;
        }
        if ((c_center - rect.max).magnitude <= c_radius) {
            return true;
        }
        if ((c_center - new Vector2(rect.xMin, rect.yMax)).magnitude <= c_radius) {
            return true;
        }
        if ((c_center - new Vector2(rect.xMax, rect.yMin)).magnitude <= c_radius) {
            return true;
        }
        if (new Rect(rect.xMin, rect.yMin - c_radius, rect.width, rect.height + c_radius * 2).Contains(c_center)) {
            return true;
        }
        return new Rect(rect.xMin - c_radius, rect.yMin, rect.width + c_radius * 2, rect.height).Contains(c_center);
    }

    private bool quadtree_traverse(Vector2 position, int LOD_level, Vector3 view_position, List<Matrix4x4> transforms_acc, List<float> LOD_levels_acc, List<float> masks_acc) { // frustrum culling is also a good addition.
        if (Mathf.Abs(view_position.y) > LOD_ranges[LOD_level]) { // if the plane itself is not in view range
            return false;
        }
        float y_adjusted_LOD_range = Mathf.Sqrt(LOD_ranges[LOD_level] * LOD_ranges[LOD_level] - view_position.y * view_position.y);
        float cell_size = min_LOD_cell_size * Mathf.Pow(2.0f, LOD_level);
        Rect rect = new Rect(position.x, position.y, cell_size, cell_size);
        Vector2 view_proj = new Vector2(view_position.x, view_position.z);

        bool intersect = circle_intersect_AArect(rect, view_proj, y_adjusted_LOD_range);
        
        if (!intersect) {
            return false;
        }

        Matrix4x4 translation = Matrix4x4.Translate(new Vector3(position.x, 0.0f, position.y));
        Matrix4x4 scale = Matrix4x4.Scale(new Vector3(cell_size, 1.0f, cell_size));

        if (LOD_level == 0) {
            transforms_acc.Add(translation * scale);
            LOD_levels_acc.Add(LOD_level);
            //masks_acc.Add((float)MASK_FLAGS.NONE);
            return true;
        }

        float LOD_range_next = Mathf.Sqrt(LOD_ranges[LOD_level-1] * LOD_ranges[LOD_level-1] - view_position.y * view_position.y);
        bool intersect_next = circle_intersect_AArect(rect, view_proj, LOD_range_next);

        if (!intersect_next) {
            transforms_acc.Add(translation * scale);
            LOD_levels_acc.Add(LOD_level);
            //masks_acc.Add((float)MASK_FLAGS.NONE);
        } else {

            bool tl_child = quadtree_traverse(position, LOD_level - 1, view_position, transforms_acc, LOD_levels_acc, masks_acc);
            bool tr_child = quadtree_traverse(position + new Vector2(cell_size / 2.0f, 0.0f), LOD_level - 1, view_position, transforms_acc, LOD_levels_acc, masks_acc);
            bool bl_child = quadtree_traverse(position + new Vector2(0.0f, cell_size / 2.0f), LOD_level - 1, view_position, transforms_acc, LOD_levels_acc, masks_acc);
            bool br_child = quadtree_traverse(position + new Vector2(cell_size, cell_size) / 2.0f, LOD_level - 1, view_position, transforms_acc, LOD_levels_acc, masks_acc);

            // UNCOMMENT BELOW IF USING MASKS
            /*
            MASK_FLAGS mask = MASK_FLAGS.NONE;

            if (tl_child) {
                mask = mask | MASK_FLAGS.TL;
            }
            if (tr_child) {
                mask = mask | MASK_FLAGS.TR;
            }
            if (bl_child) {
                mask = mask | MASK_FLAGS.BL;
            }
            if (br_child) {
                mask = mask | MASK_FLAGS.BR;
            }

            if (mask != MASK_FLAGS.ALL) {
                transforms_acc.Add(translation * scale);
                LOD_levels_acc.Add(LOD_level);
                masks_acc.Add((float)mask);
            }
            */

            Matrix4x4 child_scale = Matrix4x4.Scale(new Vector3(cell_size / 2.0f, 1.0f, cell_size / 2.0f));

            // if we covered for our children this is where we would do it. For now we will make them cover for themselves.
            if (!tl_child) {
                transforms_acc.Add(translation * child_scale);
                LOD_levels_acc.Add(LOD_level - 1);
            }
            if (!tr_child) {
                Matrix4x4 tr_translation = Matrix4x4.Translate(new Vector3(position.x + cell_size / 2.0f, 0.0f, position.y));
                transforms_acc.Add(tr_translation * child_scale);
                LOD_levels_acc.Add(LOD_level - 1);
            }
            if (!bl_child) {
                Matrix4x4 bl_translation = Matrix4x4.Translate(new Vector3(position.x, 0.0f, position.y + cell_size / 2.0f));
                transforms_acc.Add(bl_translation * child_scale);
                LOD_levels_acc.Add(LOD_level - 1);
            }
            if (!br_child) {
                Matrix4x4 br_translation = Matrix4x4.Translate(new Vector3(position.x + cell_size / 2.0f, 0.0f, position.y + cell_size / 2.0f));
                transforms_acc.Add(br_translation * child_scale);
                LOD_levels_acc.Add(LOD_level - 1);
            }
        }
        return true;
    }
}
