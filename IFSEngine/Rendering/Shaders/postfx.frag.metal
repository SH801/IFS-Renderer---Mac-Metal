#include <metal_stdlib>
#include <simd/simd.h>

using namespace metal;

struct Params
{
    int width;
    int height;
    float4 postfx_real_params[64];
    float3 postfx_vec3_params[64];
    int palette_colors_count;
};

struct palette_ubo
{
    float4 palette[150];
};

@includes

// 1. GLOBAL HELPER FUNCTIONS (Place here, completely outside main0)
inline float3 lookupPaletteColor(float index, constant palette_ubo& palette_buf, int count) {
    int idx = clamp(int(index), 0, count - 1);
    return palette_buf.palette[idx].rgb;
}

fragment float4 main0(constant Params& params [[buffer(0)]], 
                      constant palette_ubo& _palette_ubo [[buffer(5)]], 
                      texture2d<float> input_tex [[texture(0)]], 
                      sampler input_texSmplr [[sampler(0)]], 
                      float4 gl_FragCoord [[position]])
{
    float2 uv = float2(gl_FragCoord.x / float(params.width), gl_FragCoord.y / float(params.height));
    float4 input_color = input_tex.sample(input_texSmplr, uv);

    @PostFxSnippet

    return output_color;
}