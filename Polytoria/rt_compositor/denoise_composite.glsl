#[compute]
#version 460

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(rgba16f, set = 0, binding = 1) uniform image2D refl_image;
layout(rgba16f, set = 0, binding = 3) uniform image2D gi_image;
layout(rgba16f, set = 0, binding = 4) uniform image2D gi_albedo;
layout(rgba32f, set = 0, binding = 5) uniform image2D gi_pos;
layout(rgba16f, set = 0, binding = 7) uniform image2D vol_scatter;
layout(rgba16f, set = 0, binding = 8) uniform image2D gi_normal_full;
layout(rgba32f, set = 0, binding = 9) uniform image2D gi_pos_half;
layout(rgba16f, set = 0, binding = 10) uniform image2D gi_normal_half;

layout(push_constant, std430) uniform Params {
	uvec2 size;
	uint pad0;
	uint pad1;
} params;

const float GI_APPLY = 0.95;
const float VIGNETTE_STRENGTH = 1.4;
const float UP_SIGMA_N = 16.0;
const float UP_SIGMA_Z = 0.3;

const int DEBUG_MODE = 0;
const float DEBUG_GAIN = 4.0;

vec3 upsample_gi(ivec2 p) {
	ivec2 hmax = ivec2((params.size + uvec2(1u)) / 2u) - ivec2(1);
	ivec2 hbase = clamp(p >> 1, ivec2(0), hmax);
	vec3 fn = imageLoad(gi_normal_full, p).xyz;
	if (dot(fn, fn) < 1e-6) {
		return imageLoad(gi_image, hbase).rgb;
	}
	vec3 fpos = imageLoad(gi_pos, p).xyz;
	vec3 acc = vec3(0.0);
	float wsum = 0.0;
	for (int dy = -1; dy <= 1; dy++) {
		for (int dx = -1; dx <= 1; dx++) {
			ivec2 hq = clamp(hbase + ivec2(dx, dy), ivec2(0), hmax);
			vec3 hn = imageLoad(gi_normal_half, hq).xyz;
			if (dot(hn, hn) < 1e-6) {
				continue;
			}
			vec3 hpos = imageLoad(gi_pos_half, hq).xyz;
			float wn = pow(max(dot(fn, hn), 0.0), UP_SIGMA_N);
			float wz = exp(-abs(dot(fn, hpos - fpos)) / UP_SIGMA_Z);
			float w = wn * wz + 1e-5;
			acc += imageLoad(gi_image, hq).rgb * w;
			wsum += w;
		}
	}
	if (wsum < 1e-4) {
		return imageLoad(gi_image, hbase).rgb;
	}
	return acc / wsum;
}

void main() {
	uvec2 gid = gl_GlobalInvocationID.xy;
	if (gid.x >= params.size.x || gid.y >= params.size.y) {
		return;
	}
	ivec2 p = ivec2(gid);

	if (DEBUG_MODE == 1) {
		imageStore(color_image, p, vec4(upsample_gi(p) * DEBUG_GAIN, 1.0));
		return;
	}
	if (DEBUG_MODE == 2) {
		imageStore(color_image, p, vec4(fract(imageLoad(gi_pos, p).rgb * 0.2), 1.0));
		return;
	}

	vec3 col = imageLoad(color_image, p).rgb;
	vec3 irradiance = upsample_gi(p);
	col += irradiance * imageLoad(gi_albedo, p).rgb * GI_APPLY;

	vec4 center = imageLoad(refl_image, p);
	if (center.a > 0.0) {
		col = mix(col, center.rgb, center.a);
	}

	col += imageLoad(vol_scatter, p).rgb;

	vec2 vd = (vec2(p) + vec2(0.5)) / vec2(params.size) - vec2(0.5);
	col *= clamp(1.0 - dot(vd, vd) * VIGNETTE_STRENGTH, 0.0, 1.0);

	imageStore(color_image, p, vec4(col, 1.0));
}
