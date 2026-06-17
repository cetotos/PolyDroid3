#[compute]
#version 460

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D refl_in;
layout(rgba16f, set = 0, binding = 1) uniform image2D refl_out;
layout(rgba32f, set = 0, binding = 2) uniform image2D gi_pos;
layout(rgba16f, set = 0, binding = 3) uniform image2D gi_normal;
layout(rgba16f, set = 0, binding = 4) uniform image2D data_img;

layout(push_constant, std430) uniform Params {
	uvec2 size;
	uint step;
	uint pad;
} params;

const float SIGMA_N = 64.0;
const float SIGMA_Z = 0.4;
const float ROUGH_STEP = 0.12;
const float kernel[5] = float[5](1.0, 4.0, 6.0, 4.0, 1.0);

void main() {
	uvec2 gid = gl_GlobalInvocationID.xy;
	if (gid.x >= params.size.x || gid.y >= params.size.y) {
		return;
	}
	ivec2 p = ivec2(gid);
	ivec2 maxp = ivec2(params.size) - ivec2(1);

	vec4 center = imageLoad(refl_in, p);
	float rough = imageLoad(data_img, p).r;
	vec3 cn = imageLoad(gi_normal, p).xyz;

	if (center.a <= 0.0 || dot(cn, cn) < 1e-6 || rough < ROUGH_STEP * float(params.step)) {
		imageStore(refl_out, p, center);
		return;
	}

	vec3 cpos = imageLoad(gi_pos, p).xyz;
	int st = int(params.step);
	vec3 sum = center.rgb * kernel[2] * kernel[2];
	float wsum = kernel[2] * kernel[2];

	for (int dy = -2; dy <= 2; dy++) {
		for (int dx = -2; dx <= 2; dx++) {
			if (dx == 0 && dy == 0) {
				continue;
			}
			ivec2 q = clamp(p + ivec2(dx, dy) * st, ivec2(0), maxp);
			vec4 qc = imageLoad(refl_in, q);
			if (qc.a <= 0.0) {
				continue;
			}
			vec3 qn = imageLoad(gi_normal, q).xyz;
			if (dot(qn, qn) < 1e-6) {
				continue;
			}
			vec3 qpos = imageLoad(gi_pos, q).xyz;
			float kw = kernel[dx + 2] * kernel[dy + 2];
			float wn = pow(max(0.0, dot(cn, qn)), SIGMA_N);
			float wz = exp(-abs(dot(cn, qpos - cpos)) / (SIGMA_Z + 1e-4));
			float w = kw * wn * wz;
			sum += qc.rgb * w;
			wsum += w;
		}
	}

	imageStore(refl_out, p, vec4(sum / wsum, center.a));
}
