#[compute]
#version 460

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D gi_cur;
layout(rgba16f, set = 0, binding = 1) uniform image2D gi_hist;
layout(rgba32f, set = 0, binding = 2) uniform image2D gi_pos;
layout(rgba16f, set = 0, binding = 3) uniform image2D gi_out;
layout(rgba32f, set = 0, binding = 5) uniform image2D gi_pos_prev;
layout(rgba32f, set = 0, binding = 6) uniform image2D gi_prevpos;
layout(rgba16f, set = 0, binding = 7) uniform image2D gi_normal;

layout(set = 0, binding = 4, std140) uniform Camera {
	vec4 origin;
	vec4 right;
	vec4 up;
	vec4 forward;
	mat4 view_proj;
	vec4 light_dir;
	vec4 sky_top;
	vec4 sky_bottom;
	vec4 sky_horizon;
	mat4 inv_projection;
	vec4 proxy_tint;
	mat4 prev_view_proj;
	vec4 sun_color;
} cam;

layout(push_constant, std430) uniform Params {
	// this push-constant matrix arrives as zeros, so we read prev_view_proj from the camera ubo (cam.prev_view_proj) instead. leave this one unused
	mat4 unused_prev_view_proj;
	uvec2 size;
	uint pad0;
	uint pad1;
} params;

const float HIST_WEIGHT = 0.92;
const int DEBUG_REPROJ = 0;

void main() {
	uvec2 gid = gl_GlobalInvocationID.xy;
	if (gid.x >= params.size.x || gid.y >= params.size.y) {
		return;
	}
	ivec2 p = ivec2(gid);
	ivec2 maxp = ivec2(params.size) - ivec2(1);

	vec3 cur = imageLoad(gi_cur, p).rgb;

	vec3 nmin = cur;
	vec3 nmax = cur;
	for (int dy = -1; dy <= 1; dy++) {
		for (int dx = -1; dx <= 1; dx++) {
			vec3 s = imageLoad(gi_cur, clamp(p + ivec2(dx, dy), ivec2(0), maxp)).rgb;
			nmin = min(nmin, s);
			nmax = max(nmax, s);
		}
	}
	vec3 ext = (nmax - nmin) * 0.45 + vec3(0.01);
	nmin -= ext;
	nmax += ext;

	vec3 wpos = imageLoad(gi_pos, p).xyz;
	vec3 prev_wpos = imageLoad(gi_prevpos, p).xyz;
	float weight = HIST_WEIGHT;
	vec3 hist = cur;
	vec4 pclip = cam.prev_view_proj * vec4(prev_wpos, 1.0);
	if (pclip.w > 0.0001) {
		vec2 puv = (pclip.xy / pclip.w) * 0.5 + 0.5;
		if (puv.x >= 0.0 && puv.x <= 1.0 && puv.y >= 0.0 && puv.y <= 1.0) {
			ivec2 hp = clamp(ivec2(puv * vec2(params.size)), ivec2(0), maxp);
			vec3 prev_pos = imageLoad(gi_pos_prev, hp).xyz;
			vec3 n_cur = imageLoad(gi_normal, p).xyz;
			float cam_dist = length(wpos - cam.origin.xyz);
			float pos_tol = 0.05 * cam_dist + 0.1;
			float plane_tol = 0.03 * cam_dist + 0.05;
			float plane_dist = abs(dot(n_cur, prev_pos - wpos));
			if (length(prev_pos - prev_wpos) > pos_tol || plane_dist > plane_tol) {
				weight = 0.0;
			} else {
				hist = imageLoad(gi_hist, hp).rgb;
			}
		} else {
			weight = 0.0;
		}
	} else {
		weight = 0.0;
	}

	if (DEBUG_REPROJ == 1) {
		vec3 dbg = vec3(0.0, 1.0, 0.0);
		if (pclip.w <= 0.0001) {
			dbg = vec3(1.0, 0.0, 0.0);
		} else {
			vec2 dpuv = (pclip.xy / pclip.w) * 0.5 + 0.5;
			if (dpuv.x < 0.0 || dpuv.x > 1.0 || dpuv.y < 0.0 || dpuv.y > 1.0) {
				dbg = vec3(0.0, 0.0, 1.0);
			}
		}
		imageStore(gi_out, p, vec4(dbg, 1.0));
		return;
	}

	hist = clamp(hist, nmin, nmax);
	vec3 outc = mix(cur, hist, weight);
	if (any(isnan(outc)) || any(isinf(outc))) outc = vec3(0.0);
	imageStore(gi_out, p, vec4(outc, 1.0));
}
