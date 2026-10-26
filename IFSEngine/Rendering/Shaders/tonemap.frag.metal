#pragma clang diagnostic ignored "-Wmissing-prototypes"

#include <metal_stdlib>
#include <simd/simd.h>

using namespace metal;

struct Params
{
    int width;
    int height;
    uint max_density;
    float brightness;
    float inv_gamma;
    float gamma_threshold;
    float vibrancy;
    float3 bg_color;
};

struct histogram_buffer
{
    float4 histogram[1];
};

struct main0_out
{
    float4 color [[color(0)]];
};

static inline __attribute__((always_inline))
float4 logscale(thread const float4& acc, constant Params& params)
{
    float param = 1.0 + ((params.brightness * acc.w) / float(params.max_density));
    float ls = log10(param) / acc.w;
    float4 lh = acc * ls;
    return lh;
}

static inline __attribute__((always_inline))
float4 tonemap(thread const float4& fp, constant Params& params)
{
    float funcval = 0.0;
    if (params.gamma_threshold != 0.0)
    {
        funcval = powr(params.gamma_threshold, params.inv_gamma - 1.0);
    }
    float alpha;
    if (fp.w < params.gamma_threshold)
    {
        float frac = fp.w / params.gamma_threshold;
        alpha = (((1.0 - frac) * fp.w) * funcval) + (frac * powr(fp.w, params.inv_gamma));
    }
    else
    {
        alpha = powr(fp.w, params.inv_gamma);
    }
    float ls = (params.vibrancy * alpha) / fp.w;
    alpha = fast::clamp(alpha, 0.0, 1.0);
    float3 o = (fp.xyz * ls) + (powr(fp.xyz, float3(params.inv_gamma)) * (1.0 - params.vibrancy));
    o = fast::clamp(o, float3(0.0), float3(1.0));
    o += (params.bg_color * (1.0 - alpha));
    o = fast::clamp(o, float3(0.0), float3(1.0));
    return float4(o, alpha);
}

fragment main0_out main0(constant Params& params [[buffer(0)]], const device histogram_buffer& _208 [[buffer(1)]], float4 gl_FragCoord [[position]])
{
    main0_out out = {};
    int px = int(gl_FragCoord.x);
    int py = int(gl_FragCoord.y);
    float2 uv = float2(gl_FragCoord.x / float(params.width), gl_FragCoord.y / float(params.height));
    int pxi = px + (py * params.width);
    float4 c = _208.histogram[pxi];
    float4 param = c;
    c = logscale(param, params);
    float4 param_1 = c;
    c = tonemap(param_1, params);
    out.color = c;
    return out;
}

