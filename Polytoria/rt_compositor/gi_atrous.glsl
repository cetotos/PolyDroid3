#[compute]
#version 460

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D col_in;
layout(rgba16f, set = 0, binding = 1) uniform image2D col_out;
layout(rgba32f, set = 0, binding = 2) uniform image2D gi_pos;
layout(rgba16f, set = 0, binding = 3) uniform image2D gi_normal;

layout(push_constant, std430) uniform Params {
	uvec2 size;
	uint step;
	uint pad;
} params;

const float SIGMA_N = 64.0;
const float SIGMA_Z = 0.4;
const float kernel[5] = float[5](1.0, 4.0, 6.0, 4.0, 1.0);

void main() {
	uvec2 gid = gl_GlobalInvocationID.xy;
	if (gid.x >= params.size.x || gid.y >= params.size.y) {
		return;
	}
	ivec2 p = ivec2(gid);
	ivec2 maxp = ivec2(params.size) - ivec2(1);

	vec3 ccol = imageLoad(col_in, p).rgb;
	vec3 cpos = imageLoad(gi_pos, p).xyz;
	vec3 cn = imageLoad(gi_normal, p).xyz;

	if (dot(cn, cn) < 1e-6) {
		imageStore(col_out, p, vec4(ccol, 1.0));
		return;
	}

	int st = int(params.step);
	vec3 sum = ccol * kernel[2] * kernel[2];
	float wsum = kernel[2] * kernel[2];

	for (int dy = -2; dy <= 2; dy++) {
		for (int dx = -2; dx <= 2; dx++) {
			if (dx == 0 && dy == 0) {
				continue;
			}
			ivec2 q = clamp(p + ivec2(dx, dy) * st, ivec2(0), maxp);
			vec3 qn = imageLoad(gi_normal, q).xyz;
			if (dot(qn, qn) < 1e-6) {
				continue;
			}
			vec3 qcol = imageLoad(col_in, q).rgb;
			vec3 qpos = imageLoad(gi_pos, q).xyz;

			float kw = kernel[dx + 2] * kernel[dy + 2];
			float wn = pow(max(0.0, dot(cn, qn)), SIGMA_N);
			float plane_dist = abs(dot(cn, qpos - cpos));
			float wz = exp(-plane_dist / (SIGMA_Z + 1e-4));
			float w = kw * wn * wz;
			sum += qcol * w;
			wsum += w;
		}
	}

	vec3 outc = wsum > 1e-6 ? sum / wsum : ccol;
	imageStore(col_out, p, vec4(outc, 1.0));
}
