#pragma clang diagnostic ignored "-Wmissing-prototypes"

#include <metal_stdlib>
#include <simd/simd.h>

using namespace metal;

// Helper macros and constants for transforms
#define atan(y, x) atan2(y, x)
#define PI 3.14159265358979323846f
#define TWOPI (2.0f * PI)
#define DEGTORAD (PI / 180.0f)

// GLSL to Metal type bridges
typedef float2 vec2;
typedef float3 vec3;
typedef float4 vec4;
typedef float3x3 mat3;
typedef bool3 bvec3;

// Bridge GLSL logical / mixing functions to Metal
#define notEqual(x, y) ((x) != (y))

// Overload mix to support GLSL-style boolean condition selection alongside float interpolation
inline float3 mix(float3 x, float3 y, bool3 cond) {
    return select(x, y, cond);
}

inline float2 mix(float2 x, float2 y, bool2 cond) {
    return select(x, y, cond);
}

inline float4 mix(float4 x, float4 y, bool4 cond) {
    return select(x, y, cond);
}

inline float mix(float x, float y, bool cond) {
    return cond ? y : x;
}

// --- GLSL Function & Operator Mappings ---
#define mod(x, y) fmod(x, y)
#define lessThan(x, y) ((x) < (y))
#define greaterThan(x, y) ((x) > (y))
#define lessThanEqual(x, y) ((x) <= (y))
#define greaterThanEqual(x, y) ((x) >= (y))
#define equal(x, y) ((x) == (y))
#define notEqual(x, y) ((x) != (y))

// --- Sentinel Value ---
constant float3 discarded_point = float3(0.0f);

// --- Hash Function Fallbacks (if not already provided by @includes) ---
inline float f_hash2(float x, float y) {
    return fract(sin(dot(float2(x, y), float2(12.9898f, 78.233f))) * 43758.5453f);
}

inline float f_hash3(float x, float y, float z) {
    return fract(sin(dot(float3(x, y, z), float3(12.9898f, 78.233f, 37.719f))) * 43758.5453f);
}


struct real_params_ubo
{
    float real_params[200];
};

struct vec3_params_ubo
{
    float4 vec3_params[200];
};

struct Iterator
{
    float color_speed;
    float color_index;
    float opacity;
    float reset_prob;
    int reset_alias;
    int tfId;
    int real_params_index;
    int vec3_params_index;
    int shading_mode;
    float tf_mix;
    float tf_add;
    int padding2;
};

struct p_state
{
    float4 pos;
    float color_index;
    float dummy0;
    int iterator_index;
    int iteration_depth;
};

struct camera_params
{
    float4x4 view_proj_mat;
    float4 position;
    float4 forward;
    float4 focus_point;
    float aperture;
    float focus_distance;
    float depth_of_field;
    int projection_type;
};

struct Globals
{
    uint seed;
    int width;
    int height;
    int dispatch_cnt;
    int reset_points_state;
    int invocation_iters;
};

struct camera_params_1
{
    float4x4 view_proj_mat;
    float4 position;
    float4 forward;
    float4 focus_point;
    float aperture;
    float focus_distance;
    float depth_of_field;
    int projection_type;
};

struct settings_ubo
{
    camera_params_1 camera;
    float fog_effect;
    int itnum;
    int palettecnt;
    int mark_area_in_focus;
    int warmup;
    float entropy;
    int max_filter_radius;
    int padding0;
    int filter_method;
    float filter_param0;
    float filter_param1;
    float filter_param2;
};

struct palette_ubo
{
    float4 palette[150];
};

struct Iterator_1
{
    float color_speed;
    float color_index;
    float opacity;
    float reset_prob;
    int reset_alias;
    int tfId;
    int real_params_index;
    int vec3_params_index;
    int shading_mode;
    float tf_mix;
    float tf_add;
    int padding2;
};

struct iterators_ubo
{
    Iterator_1 iterators[100];
};

struct alias_tables_ubo
{
    float4 alias_tables[100];
};

struct histogram_buffer
{
    float4 histogram[1];
};

struct p_state_1
{
    float4 pos;
    float color_index;
    float dummy0;
    int iterator_index;
    int iteration_depth;
};

struct points_buffer
{
    p_state_1 state[1];
};

constant uint3 gl_WorkGroupSize [[maybe_unused]] = uint3(64u, 1u, 1u);

static inline __attribute__((always_inline))
uint pcg_hash(thread const uint& x)
{
    uint state = (x * 747796405u) + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

static inline __attribute__((always_inline))
uint4 pcg_hash_4d(thread uint4& v)
{
    v = (v * uint4(1664525u)) + uint4(1013904223u);
    v.x += (v.y * v.w);
    v.y += (v.z * v.x);
    v.z += (v.x * v.y);
    v.w += (v.y * v.z);
    v ^= (v >> uint4(16u));
    v.x += (v.y * v.w);
    v.y += (v.z * v.x);
    v.z += (v.x * v.y);
    v.w += (v.y * v.z);
    return v;
}

static inline __attribute__((always_inline))
uint xorshift128(thread uint4& v)
{
    v.w ^= (v.w << 11u);
    v.w ^= (v.w >> 8u);
    v = v.wxyz;
    v.x ^= v.y;
    v.x ^= (v.y >> 19u);
    return v.x;
}

static inline __attribute__((always_inline))
float f_hash(thread uint& h)
{
    h &= 8388607u;
    h |= 1065353216u;
    float r2 = as_type<float>(h);
    return r2 - 1.0;
}

static inline __attribute__((always_inline))
float f_hash4(thread const uint& u1, thread const uint& u2, thread const uint& u3, thread const uint& u4)
{
    uint4 param = uint4(u1, u2, u3, u4);
    uint4 _442 = pcg_hash_4d(param);
    uint4 param_1 = _442;
    uint _444 = xorshift128(param_1);
    uint param_2 = _444;
    float _446 = f_hash(param_2);
    return _446;
}

static inline __attribute__((always_inline))
float random(thread uint& nextSample, constant Globals& _451, thread uint3& gl_GlobalInvocationID)
{
    uint _461 = nextSample;
    nextSample = _461 + uint(1);
    uint param = _451.seed;
    uint param_1 = gl_GlobalInvocationID.x;
    uint param_2 = uint(_451.dispatch_cnt);
    uint param_3 = _461;
    return f_hash4(param, param_1, param_2, param_3);
}

static inline __attribute__((always_inline))
float startingDistribution(thread const float& uniformR)
{
    float a = powr(uniformR, 0.3333333432674407958984375);
    float curve = 1.57842504978179931640625;
    return ((-1.0) / curve) * log(1.0 - a);
}

static inline __attribute__((always_inline))
int alias_sample(thread const float& r01, constant settings_ubo& settings, constant iterators_ubo& _898)
{
    int i = int(floor(float(settings.itnum) * r01));
    float y = fract(float(settings.itnum) * r01);
    if (y < _898.iterators[i].reset_prob)
    {
        return i;
    }
    return _898.iterators[i].reset_alias;
}

static inline __attribute__((always_inline))
p_state reset_state(thread const uint& workgroup_hash_next, thread uint& invocation_hash_next, constant Globals& _451, thread uint3& gl_GlobalInvocationID, constant settings_ubo& settings, constant iterators_ubo& _898, thread uint3& gl_WorkGroupID)
{
    uint param = invocation_hash_next;
    float _976 = random(param, _451, gl_GlobalInvocationID);
    invocation_hash_next = param;
    float theta = 6.283185482025146484375 * _976;
    uint param_1 = invocation_hash_next;
    float _982 = random(param_1, _451, gl_GlobalInvocationID);
    invocation_hash_next = param_1;
    float phi = acos((2.0 * _982) - 1.0);
    uint param_2 = invocation_hash_next;
    float _990 = random(param_2, _451, gl_GlobalInvocationID);
    invocation_hash_next = param_2;
    float param_3 = _990;
    float rho = startingDistribution(param_3);
    rho *= (2.0 * length(settings.camera.position));
    float sin_phi = sin(phi);
    p_state p;
    p.pos = float4((rho * sin_phi) * cos(theta), (rho * sin_phi) * sin(theta), rho * cos(phi), 0.0);
    uint param_4 = _451.seed;
    uint param_5 = gl_WorkGroupID.x;
    uint param_6 = uint(_451.dispatch_cnt);
    uint param_7 = workgroup_hash_next;
    float workgroup_random = f_hash4(param_4, param_5, param_6, param_7);
    float param_8 = workgroup_random;
    p.iterator_index = alias_sample(param_8, settings, _898);
    p.color_index = _898.iterators[p.iterator_index].color_index;
    p.iteration_depth = 0;
    return p;
}

static inline __attribute__((always_inline))
int alias_sample_xaos(thread const int& iterator_index, thread const float& r01, constant settings_ubo& settings, constant alias_tables_ubo& _932)
{
    int i = int(floor(float(settings.itnum) * r01));
    float y = fract(float(settings.itnum) * r01);
    float4 t = _932.alias_tables[(iterator_index * settings.itnum) + i];
    float prob = t.x;
    int alias = int(t.y);
    if (y < prob)
    {
        return i;
    }
    return alias;
}

static inline __attribute__((always_inline))
void apply_coloring(thread const Iterator& it, thread const float4& p0, thread const float4& p, thread float& color_index)
{
    float in_color = color_index;
    float speed = it.color_speed;
    if (it.shading_mode == 1)
    {
        float p_delta = length(p - p0);
        speed *= (1.0 / (1.0 + p_delta));
    }
    float new_index = mix(in_color, it.color_index, speed);
    bool _517 = new_index != 0.0;
    bool _523;
    if (_517)
    {
        _523 = fract(new_index) == 0.0;
    }
    else
    {
        _523 = _517;
    }
    if (_523)
    {
        color_index = new_index;
    }
    else
    {
        color_index = fract(new_index);
    }
}

static inline __attribute__((always_inline))
float3x3 rotmat(thread const float3& v, thread const float& arad)
{
    float c = cos(arad);
    float s = sin(arad);
    return float3x3(float3(c + (((1.0 - c) * v.x) * v.x), (((1.0 - c) * v.x) * v.y) - (s * v.z), (((1.0 - c) * v.x) * v.z) + (s * v.y)), float3((((1.0 - c) * v.x) * v.y) + (s * v.z), c + (((1.0 - c) * v.y) * v.y), (((1.0 - c) * v.y) * v.z) - (s * v.x)), float3((((1.0 - c) * v.x) * v.z) - (s * v.y), (((1.0 - c) * v.y) * v.z) + (s * v.x), c + (((1.0 - c) * v.z) * v.z)));
}

static inline __attribute__((always_inline))
float3x3 rotate_euler(thread const float3& euler_angles)
{
    float3 param = float3(1.0, 0.0, 0.0);
    float param_1 = euler_angles.x;
    float3 param_2 = float3(0.0, 1.0, 0.0);
    float param_3 = euler_angles.y;
    float3 param_4 = float3(0.0, 0.0, 1.0);
    float param_5 = euler_angles.z;
    return (rotmat(param, param_1) * rotmat(param_2, param_3)) * rotmat(param_4, param_5);
}

static inline __attribute__((always_inline))
float2 project_equirectangular(thread const camera_params& c, thread const float3& p_ndc, thread float& weight)
{
    float3 dir = fast::normalize(p_ndc);
    float3 param = float3(-1.57079637050628662109375, 1.57079637050628662109375, 0.0);
    dir = rotate_euler(param) * dir;
    if (dir.x == 0.0)
    {
        return float2(-2.0);
    }
    float latitude = -asin(dir.z);
    weight = 1.0 / cos(latitude);
    return float2(precise::atan2(dir.y, dir.x) / 3.1415927410125732421875, latitude / 3.1415927410125732421875);
}

static inline __attribute__((always_inline))
float2 project_fisheye(thread const camera_params& c, thread const float4& p_hom)
{
    if (p_hom.z >= 0.0)
    {
        return float2(-2.0);
    }
    float3 p_ndc = p_hom.xyz / float3(p_hom.w);
    p_ndc /= float3(p_ndc.z);
    float r = precise::atan2(sqrt((p_ndc.x * p_ndc.x) + (p_ndc.y * p_ndc.y)), p_ndc.z);
    float phi = precise::atan2(p_ndc.y, p_ndc.x);
    return float2(cos(phi), sin(phi)) * ((2.0 * r) / 3.1415927410125732421875);
}

static inline __attribute__((always_inline))
float2 project(thread const camera_params& c, thread const float3& p, thread uint& next, thread float& defocus, thread float& projection_weight, constant Globals& _451, thread uint3& gl_GlobalInvocationID)
{
    float4 p_hom = c.view_proj_mat * float4(p, 1.0);
    float2 proj;
    if (c.projection_type == 0)
    {
        float4 p_ndc = p_hom / float4(p_hom.w);
        bool _696 = any(isinf(p_ndc));
        bool _703;
        if (!_696)
        {
            _703 = any(isnan(p_ndc));
        }
        else
        {
            _703 = _696;
        }
        bool _710;
        if (!_703)
        {
            _710 = p_hom.w >= 0.0;
        }
        else
        {
            _710 = _703;
        }
        if (_710)
        {
            return float2(-2.0);
        }
        proj = p_ndc.xy;
        defocus = fast::max(0.0, abs(dot(p - c.focus_point.xyz, -c.forward.xyz)) - c.depth_of_field);
        projection_weight = 1.0;
    }
    else
    {
        if (c.projection_type == 1)
        {
            camera_params param = c;
            float3 param_1 = p_hom.xyz;
            float param_2;
            float2 _747 = project_equirectangular(param, param_1, param_2);
            projection_weight = param_2;
            proj = _747;
            defocus = fast::max(0.0, abs(distance(p, c.position.xyz) - c.focus_distance) - c.depth_of_field);
        }
        else
        {
            camera_params param_3 = c;
            float4 param_4 = p_hom;
            proj = project_fisheye(param_3, param_4);
            defocus = fast::max(0.0, abs(distance(p, c.position.xyz) - c.focus_distance) - c.depth_of_field);
            projection_weight = 1.0;
        }
    }
    uint param_5 = next;
    float _785 = random(param_5, _451, gl_GlobalInvocationID);
    next = param_5;
    float ra = _785;
    uint param_6 = next;
    float _790 = random(param_6, _451, gl_GlobalInvocationID);
    next = param_6;
    float rl = _790;
    proj += (float2(cos(ra * 6.283185482025146484375), sin(ra * 6.283185482025146484375)) * ((c.aperture * defocus) * powr(rl, 0.5)));
    bool _813 = c.projection_type == 2;
    bool _819;
    if (_813)
    {
        _819 = length(proj) > 1.0;
    }
    else
    {
        _819 = _813;
    }
    if (_819)
    {
        return float2(-2.0);
    }
    float ratio = float(_451.width) / float(_451.height);
    proj = float2(((proj.x + 1.0) * 0.5) * float(_451.width), (((proj.y * ratio) + 1.0) * 0.5) * float(_451.height));
    bool _854 = any(proj < float2(0.0));
    bool _868;
    if (!_854)
    {
        _868 = any(proj >= float2(float(_451.width), float(_451.height)));
    }
    else
    {
        _868 = _854;
    }
    if (_868)
    {
        return float2(-2.0);
    }
    return proj - float2(0.5);
}

static inline __attribute__((always_inline))
float3 getPaletteColor(thread const float& pos, constant settings_ubo& settings, constant palette_ubo& _550)
{
    float palettepos = pos * float(settings.palettecnt - 1);
    int index = int(floor(palettepos));
    float3 c1 = _550.palette[index].xyz;
    if ((index + 1) == settings.palettecnt)
    {
        return c1;
    }
    float3 c2 = _550.palette[index + 1].xyz;
    float a = fract(palettepos);
    return mix(c1, c2, float3(a));
}

static inline __attribute__((always_inline))
float Mitchell_Netravali(thread const float& x)
{
    float a = abs(x);
    if (a < 1.0)
    {
        return (((6.900000095367431640625 * ((a * a) * a)) + ((-11.8500003814697265625) * (a * a))) + 5.30000019073486328125) / 6.0;
    }
    else
    {
        if ((1.0 <= a) && (a < 2.0))
        {
            return ((((((-2.2999999523162841796875) * ((a * a) * a)) + (11.8500003814697265625 * (a * a))) + ((-19.799999237060546875) * a)) + 2.7999999523162841796875) + 7.80000019073486328125) / 6.0;
        }
        else
        {
            return 0.0;
        }
    }
}

static inline __attribute__((always_inline))
void accumulate_hit(thread const int2& proj, thread const float4& color, constant Globals& _451, volatile device histogram_buffer& _1121)
{
    // Prevent out-of-bounds writes from corrupting GPU memory
    if (proj.x < 0 || proj.x >= _451.width || proj.y < 0 || proj.y >= _451.height)
    {
        return;
    }

    int ipx = proj.x + (proj.y * _451.width);
    
    // Safety check against buffer array limits
    // (Assuming your histogram buffer is allocated for width * height elements)
    if (ipx < 0 || (unsigned int)ipx >= (unsigned int)(_451.width * _451.height))
    {
        return;
    }

    float4 _1126 = _1121.histogram[ipx];
    float3 _1128 = _1126.xyz + color.xyz;
    ((device float*)&_1121.histogram[ipx])[0u] = _1128.x;
    ((device float*)&_1121.histogram[ipx])[1u] = _1128.y;
    ((device float*)&_1121.histogram[ipx])[2u] = _1128.z;
    ((device float*)&_1121.histogram[ipx])[3u] += color.w;
}

@includes

static inline __attribute__((always_inline))
float3 apply_transform(thread const Iterator& iter, 
                       thread const p_state& _p_input, 
                       thread uint& next,
                       constant real_params_ubo& real_params_buf,
                       constant vec3_params_ubo& vec3_params_buf,
                       constant Globals& _451,
                       thread uint3& gl_GlobalInvocationID)
{
    float3 p = _p_input.pos.xyz;
    int iter_depth = _p_input.iteration_depth;

    // 1. Alias the buffers so the injected code can find 'real_params' and 'vec3_params'
    constant float* real_params = real_params_buf.real_params;
    constant float4* vec3_params = vec3_params_buf.vec3_params;

    // 2. Map single-argument random(next) to the full helper function
    #define random(n) random(n, _451, gl_GlobalInvocationID)

    // transforms gets injected right here
    @transforms

    // Clean up macro so it doesn't affect anything else
    #undef random

    return p;
}

kernel void main0(
    constant Globals& _451 [[buffer(0)]], 
    constant settings_ubo& settings [[buffer(1)]], 
    constant palette_ubo& _550 [[buffer(2)]], 
    constant iterators_ubo& _898 [[buffer(3)]], 
    constant alias_tables_ubo& _932 [[buffer(4)]], 
    volatile device histogram_buffer& _1121 [[buffer(5)]], 
    device points_buffer& _1173 [[buffer(6)]], 
    constant real_params_ubo& real_params_buf [[buffer(7)]],
    constant vec3_params_ubo& vec3_params_buf [[buffer(8)]],
    uint3 gl_GlobalInvocationID [[thread_position_in_grid]], 
    uint3 gl_WorkGroupID [[threadgroup_position_in_grid]])
{
    uint gid = gl_GlobalInvocationID.x;

    // GUARD: Prevent out-of-bounds access to the points buffer if grid exceeds point count
    // (Make sure you pass or know the max points capacity, or check against grid bounds)
    if (gid >= (_451.width * _451.height)) // Adjust if points count differs from pixel count
    {
        return;
    }

    uint param = 54321u + _451.seed;
    uint hash_base = pcg_hash(param);
    uint workgroup_hash_cnt = hash_base;
    uint invocation_hash_cnt = hash_base;
    p_state p;
    if (_451.reset_points_state == 1)
    {
        uint param_1 = workgroup_hash_cnt;
        uint param_2 = invocation_hash_cnt;
        p_state _1166 = reset_state(param_1, param_2, _451, gl_GlobalInvocationID, settings, _898, gl_WorkGroupID);
        invocation_hash_cnt = param_2;
        p = _1166;
    }
    else
    {
        p.pos = _1173.state[gid].pos;
        p.color_index = _1173.state[gid].color_index;
        p.dummy0 = _1173.state[gid].dummy0;
        p.iterator_index = _1173.state[gid].iterator_index;
        p.iteration_depth = _1173.state[gid].iteration_depth;
    }
    Iterator selected_iterator;
    camera_params param_22;
    float param_25;
    float param_26;
    for (int i = 0; i < _451.invocation_iters; i++)
    {
        workgroup_hash_cnt = hash_base + uint(i * 3);
        int next_iterator_index = -1;
        uint param_3 = _451.seed;
        uint param_4 = gl_WorkGroupID.x;
        uint param_5 = uint(_451.dispatch_cnt);
        uint param_6 = workgroup_hash_cnt + 0u;
        float r = f_hash4(param_3, param_4, param_5, param_6);
        int param_7 = p.iterator_index;
        float param_8 = r;
        next_iterator_index = alias_sample_xaos(param_7, param_8, settings, _932);
        bool _1227 = next_iterator_index == (-1);
        bool _1234;
        if (!_1227)
        {
            _1234 = p.iteration_depth == (-1);
        }
        else
        {
            _1234 = _1227;
        }
        bool _1255;
        if (!_1234)
        {
            uint param_9 = _451.seed;
            uint param_10 = gl_WorkGroupID.x;
            uint param_11 = uint(_451.dispatch_cnt);
            uint param_12 = workgroup_hash_cnt + 1u;
            _1255 = f_hash4(param_9, param_10, param_11, param_12) < settings.entropy;
        }
        else
        {
            _1255 = _1234;
        }
        if (_1255)
        {
            uint param_13 = workgroup_hash_cnt + 2u;
            uint param_14 = invocation_hash_cnt;
            p_state _1263 = reset_state(param_13, param_14, _451, gl_GlobalInvocationID, settings, _898, gl_WorkGroupID);
            invocation_hash_cnt = param_14;
            p = _1263;
        }
        else
        {
            p.iterator_index = next_iterator_index;
        }
        selected_iterator.color_speed = _898.iterators[p.iterator_index].color_speed;
        selected_iterator.color_index = _898.iterators[p.iterator_index].color_index;
        selected_iterator.opacity = _898.iterators[p.iterator_index].opacity;
        selected_iterator.reset_prob = _898.iterators[p.iterator_index].reset_prob;
        selected_iterator.reset_alias = _898.iterators[p.iterator_index].reset_alias;
        selected_iterator.tfId = _898.iterators[p.iterator_index].tfId;
        selected_iterator.real_params_index = _898.iterators[p.iterator_index].real_params_index;
        selected_iterator.vec3_params_index = _898.iterators[p.iterator_index].vec3_params_index;
        selected_iterator.shading_mode = _898.iterators[p.iterator_index].shading_mode;
        selected_iterator.tf_mix = _898.iterators[p.iterator_index].tf_mix;
        selected_iterator.tf_add = _898.iterators[p.iterator_index].tf_add;
        selected_iterator.padding2 = _898.iterators[p.iterator_index].padding2;
        float4 p0_pos = p.pos;
        Iterator param_15 = selected_iterator;
        p_state param_16 = p;
        uint param_17 = invocation_hash_cnt;
        invocation_hash_cnt = param_17;
        float3 p_ret = apply_transform(param_15, param_16, param_17, real_params_buf, vec3_params_buf, _451, gl_GlobalInvocationID);
        float3 _1325 = mix(p0_pos.xyz, p_ret + (p0_pos.xyz * selected_iterator.tf_add), float3(selected_iterator.tf_mix));
        p.pos.x = _1325.x;
        p.pos.y = _1325.y;
        p.pos.z = _1325.z;
        bool _1337 = any(isinf(p.pos.xyz));
        bool _1346;
        if (!_1337)
        {
            _1346 = any(isnan(p.pos.xyz));
        }
        else
        {
            _1346 = _1337;
        }
        bool _1358;
        if (!_1346)
        {
            _1358 = dot(p.pos.xyz, p.pos.xyz) == 0.0;
        }
        else
        {
            _1358 = _1346;
        }
        if (_1358)
        {
            p.iteration_depth = -1;
            continue;
        }
        Iterator param_18 = selected_iterator;
        float4 param_19 = p0_pos;
        float4 param_20 = p.pos;
        float param_21 = p.color_index;
        apply_coloring(param_18, param_19, param_20, param_21);
        p.color_index = param_21;
        p.iteration_depth++;
        bool _1383 = p.iteration_depth < settings.warmup;
        bool _1390;
        if (!_1383)
        {
            _1390 = selected_iterator.opacity == 0.0;
        }
        else
        {
            _1390 = _1383;
        }
        if (_1390)
        {
            continue;
        }
        param_22.view_proj_mat = settings.camera.view_proj_mat;
        param_22.position = settings.camera.position;
        param_22.forward = settings.camera.forward;
        param_22.focus_point = settings.camera.focus_point;
        param_22.aperture = settings.camera.aperture;
        param_22.focus_distance = settings.camera.focus_distance;
        param_22.depth_of_field = settings.camera.depth_of_field;
        param_22.projection_type = settings.camera.projection_type;
        float3 param_23 = p.pos.xyz;
        uint param_24 = invocation_hash_cnt;
        float2 _1425 = project(param_22, param_23, param_24, param_25, param_26, _451, gl_GlobalInvocationID);
        invocation_hash_cnt = param_24;
        float defocus = param_25;
        float projection_weight = param_26;
        float2 projf = _1425;
        if (projf.x == (-2.0))
        {
            continue;
        }
        int2 proj = int2(int(round(projf.x)), int(round(projf.y)));
        float param_27 = p.color_index;
        float4 color = float4(getPaletteColor(param_27, settings, _550), selected_iterator.opacity);
        color.w *= projection_weight;
        if (settings.fog_effect > 0.0)
        {
            float fog_mask = 2.0 * (1.0 - (1.0 / (1.0 + powr(1.0 + settings.fog_effect, -defocus))));
            fog_mask = fast::clamp(fog_mask, 0.0, 1.0);
            color.w *= fog_mask;
        }
        if (color.w == 0.0)
        {
            continue;
        }
        if ((settings.mark_area_in_focus != 0) && (defocus < 0.00999999977648258209228515625))
        {
            color = float4(1.0, 0.0, 0.0, 2.0);
        }
        float _1501 = color.w;
        float4 _1502 = color;
        float3 _1504 = _1502.xyz * _1501;
        color.x = _1504.x;
        color.y = _1504.y;
        color.z = _1504.z;
        if (settings.max_filter_radius > 0)
        {
            const int filter_radius = int(settings.max_filter_radius);

            // Loop through the full filter kernel footprint deterministically
            for (int ay = -filter_radius; ay <= filter_radius; ay++)
            {
                for (int ax = -filter_radius; ax <= filter_radius; ax++)
                {
                    int2 nb = proj + int2(ax, ay);
                    float pd = distance(float2(nb), projf);

                    // Normalize distance by filter radius for Mitchell-Netravali
                    float normalized_pd = pd / float(filter_radius);
                    float aw = fast::max(0.0, Mitchell_Netravali(normalized_pd));

                    if (nb.x >= 0 && nb.x < _451.width && nb.y >= 0 && nb.y < _451.height)
                    {
                        int2 param_31 = nb;
                        // Weight scaling factor adjusted for kernel area
                        float4 param_32 = color * (aw / float(filter_radius * filter_radius));
                        accumulate_hit(param_31, param_32, _451, _1121);
                    }
                }
            }
        }
        else
        {
            int2 param_33 = proj;
            float4 param_34 = color;
            accumulate_hit(param_33, param_34, _451, _1121);
        }
    }
    _1173.state[gid].pos = p.pos;
    _1173.state[gid].color_index = p.color_index;
    _1173.state[gid].dummy0 = p.dummy0;
    _1173.state[gid].iterator_index = p.iterator_index;
    _1173.state[gid].iteration_depth = p.iteration_depth;
}