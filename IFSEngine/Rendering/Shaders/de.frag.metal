#pragma clang diagnostic ignored "-Wmissing-prototypes"

#include <metal_stdlib>
#include <simd/simd.h>

using namespace metal;

struct Params
{
    int width;
    int height;
    float de_max_radius;
    float de_power;
    float de_threshold;
    uint max_density;
};

struct main0_out
{
    float4 color [[color(0)]];
};

static inline __attribute__((always_inline))
float4 density_estimation(thread float4& gl_FragCoord, constant Params& params, texture2d<float> histogram_tex, sampler histogram_texSmplr)
{
    int px = int(gl_FragCoord.x);
    int py = int(gl_FragCoord.y);
    float2 uv = float2(gl_FragCoord.x / float(params.width), gl_FragCoord.y / float(params.height));
    float w = (params.de_max_radius / params.de_threshold) * fast::clamp(params.de_threshold - histogram_tex.sample(histogram_texSmplr, uv).w, 0.0, 1.0);
    w = fast::clamp(w, 0.0, params.de_max_radius);
    int kSize = int(w);
    if (kSize == 0)
    {
        discard_fragment();
    }
    float4 de = float4(0.0);
    float wnorm = 0.0;
    int _94 = -kSize;
    for (int i = _94; i <= kSize; i++)
    {
        bool _106 = (px + i) < 0;
        bool _117;
        if (!_106)
        {
            _117 = (px + i) > (params.width - 1);
        }
        else
        {
            _117 = _106;
        }
        if (_117)
        {
            continue;
        }
        int _123 = -kSize;
        for (int j = _123; j <= kSize; j++)
        {
            bool _135 = (py + j) < 0;
            bool _146;
            if (!_135)
            {
                _146 = (py + j) > (params.height - 1);
            }
            else
            {
                _146 = _135;
            }
            if (_146)
            {
                continue;
            }
            float cw = fast::clamp(1.0 - (sqrt(float((i * i) + (j * j))) / (float(kSize) * 1.41421353816986083984375)), 0.0, 1.0);
            de += (histogram_tex.sample(histogram_texSmplr, (uv + (float2(float(i), float(j)) / float2(float(params.width), float(params.height))))) * cw);
            wnorm += cw;
        }
    }
    de /= float4(wnorm);
    return de;
}

fragment main0_out main0(constant Params& params [[buffer(0)]], texture2d<float> histogram_tex [[texture(0)]], sampler histogram_texSmplr [[sampler(0)]], float4 gl_FragCoord [[position]])
{
    main0_out out = {};
    float4 _204 = density_estimation(gl_FragCoord, params, histogram_tex, histogram_texSmplr);
    out.color = _204;
    return out;
}

