#[raygen]

#version 460

#pragma shader_stage(raygen)
#extension GL_EXT_ray_tracing : enable

struct Payload {
	vec3 color;
	vec3 normal;
	float t;
	float roughness;
	float metallic;
	float secondary;
	vec3 albedo;
	float glass;
	vec3 prev_pos;
};

layout(location = 0) rayPayloadEXT Payload payload;
layout(location = 1) rayPayloadEXT float shadow_payload;

layout(set = 0, binding = 0, rgba16f) uniform image2D refl_image;
layout(set = 0, binding = 26, rgba16f) uniform image2D gi_image;
layout(set = 0, binding = 27, rgba32f) uniform image2D gi_pos_image;
layout(set = 0, binding = 28, rgba16f) uniform image2D gi_albedo_image;
layout(set = 0, binding = 31, rgba32f) uniform image2D gi_prevpos_image;
layout(set = 0, binding = 32, rgba16f) uniform image2D gi_normal_image;
layout(set = 0, binding = 33, rgba16f) uniform image2D vol_scatter_image;
layout(set = 0, binding = 1) uniform accelerationStructureEXT tlas;

layout(set = 0, binding = 2, std140) uniform Camera {
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
	vec4 rt_params;
} cam;

layout(set = 0, binding = 11, rgba16f) uniform image2D data_image;
layout(set = 0, binding = 36, rgba32f) uniform image2D gi_pos_full_image;
layout(set = 0, binding = 37, rgba16f) uniform image2D gi_normal_full_image;
layout(set = 0, binding = 34, std430) readonly buffer Lights { float lights[]; };
layout(set = 0, binding = 8, rgba16f) uniform image2D scene_color;
layout(set = 0, binding = 9) uniform texture2D depth_tex;
layout(set = 0, binding = 12) uniform sampler depth_sampler;

float raster_distance(vec2 uv) {
	ivec2 px = clamp(ivec2(uv * vec2(gl_LaunchSizeEXT.xy)), ivec2(0), ivec2(gl_LaunchSizeEXT.xy) - ivec2(1));
	float d = texelFetch(sampler2D(depth_tex, depth_sampler), px, 0).r;
	vec4 ndc = vec4(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0, d, 1.0);
	vec4 view = cam.inv_projection * ndc;
	return length(view.xyz / view.w);
}

const float GI_RANGE = 25.0;
const float FIREFLY_MAX = 1.5;
const float OCCLUDED_FILL = 0.4;
const float GI_SKY_WEIGHT = 0.45;
const int DEBUG_PRIMARY = 0;

const int VOL_STEPS = 16;
const float VOL_G = 0.76;
const float VOL_MAX_DIST = 120.0;
const float VOL_PHASE_MAX = 0.3;

const int SHADOW_SAMPLES = 4;
const float LIGHT_RADIUS = 0.5;
const float SUN_SOFT = 0.03;

float hg_phase(float c, float g) {
	float g2 = g * g;
	float d = 1.0 + g2 - 2.0 * g * c;
	return (1.0 - g2) / (12.566370614 * pow(max(d, 1e-4), 1.5));
}

float gi_hash(uint n) {
	n = (n << 13u) ^ n;
	n = n * (n * n * 15731u + 789221u) + 1376312589u;
	return float(n & 0x7fffffffu) / 2147483647.0;
}

vec3 cosine_dir(vec3 n, vec3 tangent, vec3 bitangent, float u1, float u2) {
	float r = sqrt(u1);
	float phi = 6.2831853 * u2;
	vec3 tdir = vec3(r * cos(phi), r * sin(phi), sqrt(max(0.0, 1.0 - u1)));
	return normalize(tdir.x * tangent + tdir.y * bitangent + tdir.z * n);
}

vec3 gather_indirect(vec3 pos, vec3 n, uint frame) {
	vec3 up_vec = abs(n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
	vec3 tangent = normalize(cross(up_vec, n));
	vec3 bitangent = cross(n, tangent);
	vec3 ray_origin = pos + n * 0.02;
	uint seed = uint(gl_LaunchIDEXT.x) * 6151u + uint(gl_LaunchIDEXT.y) * 1597u + frame * 9277u + 7u;
	int gi_samples = int(cam.rt_params.z);
	vec3 bleed = vec3(0.0);
	for (int i = 0; i < gi_samples; i++) {
		vec3 dir = cosine_dir(n, tangent, bitangent, gi_hash(seed + uint(i) * 4u), gi_hash(seed + uint(i) * 4u + 1u));
		payload.t = -1.0;
		payload.secondary = 2.0;
		traceRayEXT(tlas, gl_RayFlagsOpaqueEXT, 0x01, 0, 0, 0, ray_origin, 0.02, dir, GI_RANGE, 0);
		if (payload.t >= 0.0) {
			vec3 first = payload.color;
			vec3 h_pos = ray_origin + dir * payload.t;
			vec3 h_n = payload.normal;
			vec3 h_albedo = payload.albedo;
			vec3 h_up = abs(h_n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
			vec3 h_tan = normalize(cross(h_up, h_n));
			vec3 h_bit = cross(h_n, h_tan);
			vec3 dir2 = cosine_dir(h_n, h_tan, h_bit, gi_hash(seed + uint(i) * 4u + 2u), gi_hash(seed + uint(i) * 4u + 3u));
			payload.t = -1.0;
			payload.secondary = 3.0;
			traceRayEXT(tlas, gl_RayFlagsOpaqueEXT, 0x01, 0, 0, 0, h_pos + h_n * 0.02, 0.02, dir2, GI_RANGE, 0);
			vec3 second = payload.color;
			vec3 contrib = first + h_albedo * second;
			float lum = dot(contrib, vec3(0.2126, 0.7152, 0.0722));
			if (lum > FIREFLY_MAX) {
				contrib *= FIREFLY_MAX / lum;
			}
			bleed += contrib;
		} else {
			bleed += payload.color * GI_SKY_WEIGHT;
		}
	}
	return bleed / float(gi_samples);
}

vec3 ggx_sample(vec3 n, float rough, float u1, float u2) {
	float a = rough * rough;
	float phi = 6.2831853 * u1;
	float ct = sqrt((1.0 - u2) / (1.0 + (a * a - 1.0) * u2));
	float st = sqrt(max(0.0, 1.0 - ct * ct));
	vec3 up_vec = abs(n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
	vec3 tang = normalize(cross(up_vec, n));
	vec3 bitang = cross(n, tang);
	return normalize(tang * (st * cos(phi)) + bitang * (st * sin(phi)) + n * ct);
}

vec3 sample_lights(vec3 pos, vec3 n) {
	int count = int(lights[0]);
	vec3 acc = vec3(0.0);
	vec3 ro = pos + n * 0.02;
	uint seed = uint(gl_LaunchIDEXT.x) * 1973u + uint(gl_LaunchIDEXT.y) * 9277u + 61u;
	for (int i = 0; i < count; i++) {
		int b = 1 + i * 8;
		vec3 lp = vec3(lights[b], lights[b + 1], lights[b + 2]);
		float range = lights[b + 3];
		vec3 lc = vec3(lights[b + 4], lights[b + 5], lights[b + 6]);
		float intensity = lights[b + 7];
		vec3 to = lp - pos;
		float dist = length(to);
		if (dist >= range || dist < 1e-3) {
			continue;
		}
		vec3 ldir = to / dist;
		float ndl = max(dot(n, ldir), 0.0);
		if (ndl <= 0.0) {
			continue;
		}
		float atten = clamp(1.0 - dist / range, 0.0, 1.0);
		atten *= atten;
		vec3 up_vec = abs(ldir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
		vec3 tang = normalize(cross(up_vec, ldir));
		vec3 bitang = cross(ldir, tang);
		float vis = 0.0;
		for (int s = 0; s < SHADOW_SAMPLES; s++) {
			float u1 = gi_hash(seed + uint(i) * 64u + uint(s) * 2u);
			float u2 = gi_hash(seed + uint(i) * 64u + uint(s) * 2u + 1u);
			float rr = LIGHT_RADIUS * sqrt(u1);
			float ph = 6.2831853 * u2;
			vec3 tgt = lp + (tang * cos(ph) + bitang * sin(ph)) * rr;
			vec3 sd = tgt - ro;
			float sdist = length(sd);
			vec3 sdir = sd / sdist;
			shadow_payload = 0.0;
			traceRayEXT(tlas,
				gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsSkipClosestHitShaderEXT | gl_RayFlagsOpaqueEXT,
				0x03, 0, 0, 1, ro, 0.02, sdir, sdist - 0.05, 1);
			vis += shadow_payload;
		}
		vis /= float(SHADOW_SAMPLES);
		acc += lc * (intensity * ndl * atten * vis);
	}
	return acc;
}

vec3 trace_reflection(vec3 refl_origin, vec3 refl_dir, vec3 cam_origin) {
	payload.t = -1.0;
	payload.secondary = 1.0;
	traceRayEXT(tlas, gl_RayFlagsOpaqueEXT, 0x03, 0, 0, 0, refl_origin, 0.02, refl_dir, 10000.0, 0);
	bool is_proxy = payload.metallic < -0.5;
	vec3 result = payload.color;
	float refl_t = payload.t;
	if (refl_t >= 0.0 && !is_proxy) {
		vec3 hit_world = refl_origin + refl_dir * refl_t;
		vec4 clip = cam.view_proj * vec4(hit_world, 1.0);
		if (clip.w > 0.0) {
			vec2 ss_uv = (clip.xy / clip.w) * 0.5 + 0.5;
			ss_uv.y = 1.0 - ss_uv.y;
			if (ss_uv.x >= 0.0 && ss_uv.x <= 1.0 && ss_uv.y >= 0.0 && ss_uv.y <= 1.0) {
				float hit_dist = length(hit_world - cam_origin);
				float vis_dist = raster_distance(ss_uv);
				if (abs(hit_dist - vis_dist) < 0.1 * hit_dist) {
					vec2 fp = ss_uv * vec2(gl_LaunchSizeEXT.xy) - vec2(0.5);
					ivec2 ip = ivec2(floor(fp));
					vec2 fr = fract(fp);
					ivec2 mx = ivec2(gl_LaunchSizeEXT.xy) - ivec2(1);
					vec3 c00 = imageLoad(scene_color, clamp(ip, ivec2(0), mx)).rgb;
					vec3 c10 = imageLoad(scene_color, clamp(ip + ivec2(1, 0), ivec2(0), mx)).rgb;
					vec3 c01 = imageLoad(scene_color, clamp(ip + ivec2(0, 1), ivec2(0), mx)).rgb;
					vec3 c11 = imageLoad(scene_color, clamp(ip + ivec2(1, 1), ivec2(0), mx)).rgb;
					result = mix(mix(c00, c10, fr.x), mix(c01, c11, fr.x), fr.y);
				}
			}
		}
	}
	return result;
}

void main() {
	const vec2 pixel_center = vec2(gl_LaunchIDEXT.xy) + vec2(0.5);
	const vec2 uv = pixel_center / vec2(gl_LaunchSizeEXT.xy);
	vec2 ndc = vec2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);

	vec3 origin = cam.origin.xyz;
	vec3 direction = normalize(cam.forward.xyz + ndc.x * cam.right.xyz + ndc.y * cam.up.xyz);

	ivec2 pixel = ivec2(gl_LaunchIDEXT.xy);

	payload.t = -1.0;
	payload.roughness = 1.0;
	payload.metallic = 0.0;
	payload.secondary = 0.0;
	traceRayEXT(tlas, gl_RayFlagsOpaqueEXT, 0x07, 0, 0, 0, origin, 0.001, direction, 10000.0, 0);

	bool dbg_hit = payload.t >= 0.0;
	bool dbg_proxy = payload.metallic < -0.5;
	bool dbg_occ = false;
	vec3 hit_prev_pos = payload.prev_pos;
	float primary_t = payload.t >= 0.0 ? payload.t : VOL_MAX_DIST;

	vec3 refl_color = vec3(0.0);
	float strength = 0.0;
	float out_roughness = 0.0;
	vec3 gi = vec3(0.0);
	vec3 primary_pos = vec3(0.0);
	vec3 primary_prev_pos = vec3(0.0);
	vec3 primary_normal = vec3(0.0);
	vec3 primary_albedo = vec3(0.0);
	vec3 prim_light = vec3(0.0);

	if (payload.t >= 0.0) {
		bool is_glass = payload.glass > 0.5;
		vec2 uv_pix = (vec2(pixel) + vec2(0.5)) / vec2(gl_LaunchSizeEXT.xy);
		float raster_dist = raster_distance(uv_pix);
		bool occluded = !is_glass && ((raster_dist < payload.t * 0.95) || (payload.t < raster_dist * 0.85));
		dbg_occ = occluded;

		if (!occluded) {
			vec3 n = payload.normal;
			primary_albedo = payload.albedo;
			primary_pos = origin + direction * payload.t;
			primary_prev_pos = hit_prev_pos;
			primary_normal = n;
			float roughness = payload.roughness;
			float metallic = payload.metallic;
			float smoothness = 1.0 - roughness;

			float cos_theta = max(dot(-direction, n), 0.0);
			float graze = pow(1.0 - cos_theta, 5.0);
			float fresnel = 0.04 + 0.96 * graze;
			float spec = smoothness * smoothness;
			float reflectivity = is_glass ? fresnel : clamp(max(metallic * spec, fresnel * spec), 0.0, 1.0);
			float refl_potential = is_glass ? 0.04 : clamp(spec * max(metallic, 0.04), 0.0, 1.0);
			float refl_rough = is_glass ? 0.0 : roughness;

			int refl_budget = int(cam.rt_params.w);
			if (refl_potential >= 0.0015 && refl_budget > 0) {
				vec3 hit_pos = origin + direction * payload.t;
				vec3 refl_origin = hit_pos + n * 0.01;
				vec3 mirror_dir = normalize(reflect(direction, n));
				int rsamples = (is_glass || refl_rough < 0.04) ? 1 : refl_budget;
				uint rseed = uint(gl_LaunchIDEXT.x) * 3041u + uint(gl_LaunchIDEXT.y) * 6791u + 13u;
				vec3 racc = vec3(0.0);
				for (int s = 0; s < rsamples; s++) {
					vec3 rdir = mirror_dir;
					if (rsamples > 1) {
						vec3 hm = ggx_sample(n, refl_rough, gi_hash(rseed + uint(s) * 2u), gi_hash(rseed + uint(s) * 2u + 1u));
						rdir = reflect(direction, hm);
						if (dot(rdir, n) <= 0.0) {
							rdir = mirror_dir;
						}
					}
					racc += trace_reflection(refl_origin, normalize(rdir), origin);
				}
				refl_color = racc / float(rsamples);
				strength = reflectivity;
				out_roughness = refl_rough;
			}
			bool gi_pixel = ((pixel.x | pixel.y) & 1) == 0;
			if (!is_glass && gi_pixel && int(cam.rt_params.z) > 0) {
				gi = gather_indirect(primary_pos, n, uint(cam.proxy_tint.w)) * cam.rt_params.x;
			}
			prim_light = primary_albedo * sample_lights(primary_pos, n);
		} else {
			primary_pos = origin + direction * payload.t;
			primary_prev_pos = hit_prev_pos;
			primary_normal = payload.normal;
			primary_albedo = payload.albedo;
			if (int(cam.rt_params.z) > 0) {
				vec3 sky_amb = (cam.sky_top.xyz + cam.sky_horizon.xyz) * 0.5;
				gi = sky_amb * OCCLUDED_FILL * cam.rt_params.x;
			}
		}
	}

	if (DEBUG_PRIMARY == 1) {
		if (!dbg_hit) gi = vec3(1.0, 0.0, 0.0);
		else if (dbg_proxy) gi = vec3(1.0, 0.0, 1.0);
		else if (dbg_occ) gi = vec3(0.0, 0.0, 1.0);
		else gi = vec3(0.0, 1.0, 0.0);
	}

	vec3 scatter = vec3(0.0);
	{
		vec3 sun_dir = normalize(cam.light_dir.xyz);
		float phase = min(hg_phase(dot(direction, sun_dir), VOL_G), VOL_PHASE_MAX);
		vec3 sun_rad = cam.sun_color.rgb * cam.sun_color.w;
		float march_end = min(primary_t, VOL_MAX_DIST);
		float dt = march_end / float(VOL_STEPS);
		float jitter = gi_hash(uint(gl_LaunchIDEXT.x) * 1973u + uint(gl_LaunchIDEXT.y) * 9277u + 7u);
		vec3 s_up = abs(sun_dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
		vec3 s_tang = normalize(cross(s_up, sun_dir));
		vec3 s_bit = cross(sun_dir, s_tang);
		uint vseed = uint(gl_LaunchIDEXT.x) * 4099u + uint(gl_LaunchIDEXT.y) * 3203u + 17u;
		if (phase > 0.02 && cam.rt_params.y > 0.0) {
			float sigma = cam.rt_params.y;
			float transmittance = 1.0;
			for (int i = 0; i < VOL_STEPS; i++) {
				float t = (float(i) + jitter) * dt;
				vec3 sp = origin + direction * t;
				float ju1 = gi_hash(vseed + uint(i) * 2u);
				float ju2 = gi_hash(vseed + uint(i) * 2u + 1u);
				float jr = SUN_SOFT * sqrt(ju1);
				float jph = 6.2831853 * ju2;
				vec3 jdir = normalize(sun_dir + (s_tang * cos(jph) + s_bit * sin(jph)) * jr);
				shadow_payload = 0.0;
				traceRayEXT(tlas,
					gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsSkipClosestHitShaderEXT | gl_RayFlagsOpaqueEXT,
					0x03, 0, 0, 1, sp, 0.02, jdir, 10000.0, 1);
				scatter += sun_rad * (shadow_payload * phase * sigma * dt * transmittance);
				transmittance *= exp(-sigma * dt);
			}
			scatter *= smoothstep(0.02, 0.08, phase);
		}
	}

	imageStore(vol_scatter_image, pixel, vec4(scatter + prim_light, 1.0));
	imageStore(refl_image, pixel, vec4(refl_color, strength));
	imageStore(gi_pos_full_image, pixel, vec4(primary_pos, 1.0));
	imageStore(gi_normal_full_image, pixel, vec4(primary_normal, 0.0));
	imageStore(gi_albedo_image, pixel, vec4(primary_albedo, 1.0));
	imageStore(data_image, pixel, vec4(out_roughness, 0.0, 0.0, 0.0));
	if (((pixel.x | pixel.y) & 1) == 0) {
		ivec2 hp = pixel >> 1;
		imageStore(gi_image, hp, vec4(gi, 1.0));
		imageStore(gi_pos_image, hp, vec4(primary_pos, 1.0));
		imageStore(gi_prevpos_image, hp, vec4(primary_prev_pos, 1.0));
		imageStore(gi_normal_image, hp, vec4(primary_normal, 0.0));
	}
}

#[miss]

#version 460
#extension GL_EXT_ray_tracing : enable

struct Payload {
	vec3 color;
	vec3 normal;
	float t;
	float roughness;
	float metallic;
	float secondary;
	vec3 albedo;
	float glass;
	vec3 prev_pos;
};

layout(location = 0) rayPayloadInEXT Payload payload;

layout(set = 0, binding = 2, std140) uniform Camera {
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
	vec4 rt_params;
} cam;

void main() {
	vec3 dir = normalize(gl_WorldRayDirectionEXT);
	float h = clamp(dir.y, 0.0, 1.0);
	vec3 sky = mix(cam.sky_top.xyz, cam.sky_bottom.xyz, pow(1.0 - h, 2.5));
	float horizon = clamp(pow(1.0 - abs(dir.y), 4.0), 0.0, 1.0);
	sky += cam.sky_horizon.xyz * horizon * 0.25;

	if (payload.secondary < 1.5) {
		vec3 sun_dir = normalize(cam.light_dir.xyz);
		vec3 sun_rad = cam.sun_color.rgb * cam.sun_color.w;
		float cos_sun = dot(dir, sun_dir);
		float glow = pow(max(cos_sun, 0.0), 16.0);
		float disk = smoothstep(0.9995, 0.9998, cos_sun);
		sky += sun_rad * (glow * 0.08 + disk * 4.0);
	}

	payload.color = sky;
	payload.t = -1.0;
	payload.roughness = 1.0;
	payload.metallic = 0.0;
}

#[closest_hit]

#version 460

#pragma shader_stage(closest_hit)
#extension GL_EXT_ray_tracing : enable

struct Payload {
	vec3 color;
	vec3 normal;
	float t;
	float roughness;
	float metallic;
	float secondary;
	vec3 albedo;
	float glass;
	vec3 prev_pos;
};

layout(location = 0) rayPayloadInEXT Payload payload;
layout(location = 1) rayPayloadEXT float shadow_payload;
hitAttributeEXT vec2 attribs;

layout(set = 0, binding = 1) uniform accelerationStructureEXT tlas;
layout(set = 0, binding = 2, std140) uniform Camera {
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
	vec4 rt_params;
} cam;
layout(set = 0, binding = 3, std430) readonly buffer Positions { float positions[]; };
layout(set = 0, binding = 4, std430) readonly buffer Indices { uint indices[]; };
layout(set = 0, binding = 5, std430) readonly buffer Offsets { uint offsets[]; };
layout(set = 0, binding = 6, std430) readonly buffer Normals { float normals[]; };
layout(set = 0, binding = 7, std430) readonly buffer Materials { float materials[]; };
layout(set = 0, binding = 10, std430) readonly buffer Colors { float colors[]; };
layout(set = 0, binding = 29, std430) readonly buffer Emissions { float emissions[]; };
layout(set = 0, binding = 13, std430) readonly buffer ChunkPositions { float chunk_positions[]; };
layout(set = 0, binding = 30, std430) readonly buffer ChunkPositionsPrev { float chunk_positions_prev[]; };
layout(set = 0, binding = 14, std430) readonly buffer ChunkNormals { float chunk_normals[]; };
layout(set = 0, binding = 15, std430) readonly buffer ChunkIndices { uint chunk_indices[]; };
layout(set = 0, binding = 16, std430) readonly buffer ChunkOffsets { uint chunk_offsets[]; };
layout(set = 0, binding = 17, std430) readonly buffer ChunkColors { float chunk_colors[]; };
layout(set = 0, binding = 18, std430) readonly buffer ChunkUvs { float chunk_uvs[]; };
layout(set = 0, binding = 19) uniform texture2D tex0;
layout(set = 0, binding = 20) uniform texture2D tex1;
layout(set = 0, binding = 21) uniform texture2D tex2;
layout(set = 0, binding = 22) uniform texture2D tex3;
layout(set = 0, binding = 23) uniform texture2D tex4;
layout(set = 0, binding = 24) uniform texture2D tex5;
layout(set = 0, binding = 25) uniform sampler tex_sampler;
layout(set = 0, binding = 34, std430) readonly buffer Lights { float lights[]; };
layout(set = 0, binding = 35, std430) readonly buffer PrevTransforms { float prev_xform[]; };
layout(set = 0, binding = 38, std430) readonly buffer WorldTexParams { float world_tex_params[]; };
layout(set = 0, binding = 39) uniform texture2DArray world_tex;
layout(set = 0, binding = 40) uniform sampler world_sampler;

vec2 part_uv(vec3 pos, vec3 normal) {
	vec3 an = abs(normal);
	vec2 uv;
	if (an.x >= an.y && an.x >= an.z) {
		uv = pos.zy;
		if (normal.x < 0.0) {
			uv.x = -uv.x;
		}
	} else if (an.y >= an.z) {
		uv = pos.xz;
		if (normal.y < 0.0) {
			uv.y = -uv.y;
		}
	} else {
		uv = pos.xy;
		if (normal.z > 0.0) {
			uv.x = -uv.x;
		}
	}
	return uv;
}

vec3 prev_world_pos(uint inst, vec3 p) {
	uint o = inst * 12u;
	vec3 c0 = vec3(prev_xform[o + 0u], prev_xform[o + 1u], prev_xform[o + 2u]);
	vec3 c1 = vec3(prev_xform[o + 3u], prev_xform[o + 4u], prev_xform[o + 5u]);
	vec3 c2 = vec3(prev_xform[o + 6u], prev_xform[o + 7u], prev_xform[o + 8u]);
	vec3 tr = vec3(prev_xform[o + 9u], prev_xform[o + 10u], prev_xform[o + 11u]);
	return c0 * p.x + c1 * p.y + c2 * p.z + tr;
}

const uint PROXY_BIT = 0x800000u;
const float AMBIENT_STRENGTH = 0.55;
const float GI_BOUNCE_AMBIENT = 0.15;
const int GI_LIGHT_LIMIT = 8;

vec3 sample_lights_gi(vec3 pos, vec3 n) {
	int count = int(lights[0]);
	int lim = min(count, GI_LIGHT_LIMIT);
	vec3 acc = vec3(0.0);
	vec3 ro = pos + n * 0.02;
	for (int i = 0; i < lim; i++) {
		int b = 1 + i * 8;
		vec3 lp = vec3(lights[b], lights[b + 1], lights[b + 2]);
		float range = lights[b + 3];
		vec3 lc = vec3(lights[b + 4], lights[b + 5], lights[b + 6]);
		float intensity = lights[b + 7];
		vec3 to = lp - pos;
		float dist = length(to);
		if (dist >= range || dist < 1e-3) {
			continue;
		}
		vec3 ldir = to / dist;
		float ndl = max(dot(n, ldir), 0.0);
		if (ndl <= 0.0) {
			continue;
		}
		float atten = clamp(1.0 - dist / range, 0.0, 1.0);
		atten *= atten;
		shadow_payload = 0.0;
		traceRayEXT(tlas,
			gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsSkipClosestHitShaderEXT | gl_RayFlagsOpaqueEXT,
			0x03, 0, 0, 1, ro, 0.02, ldir, dist - 0.05, 1);
		acc += lc * (intensity * ndl * atten * shadow_payload);
	}
	return acc;
}

vec3 sky_ambient(vec3 n) {
	float up = clamp(n.y * 0.5 + 0.5, 0.0, 1.0);
	if (up > 0.5) {
		return mix(cam.sky_horizon.xyz, cam.sky_top.xyz, (up - 0.5) * 2.0);
	}
	return mix(cam.sky_bottom.xyz * 0.25, cam.sky_horizon.xyz, up * 2.0);
}

vec3 fetch_position(uint vertex_index) {
	return vec3(positions[3u * vertex_index + 0u], positions[3u * vertex_index + 1u], positions[3u * vertex_index + 2u]);
}

vec3 fetch_normal(uint vertex_index) {
	return vec3(normals[3u * vertex_index + 0u], normals[3u * vertex_index + 1u], normals[3u * vertex_index + 2u]);
}

vec3 fetch_chunk_position(uint vertex_index) {
	return vec3(chunk_positions[3u * vertex_index + 0u], chunk_positions[3u * vertex_index + 1u], chunk_positions[3u * vertex_index + 2u]);
}

vec3 fetch_chunk_position_prev(uint vertex_index) {
	return vec3(chunk_positions_prev[3u * vertex_index + 0u], chunk_positions_prev[3u * vertex_index + 1u], chunk_positions_prev[3u * vertex_index + 2u]);
}

vec3 fetch_chunk_normal(uint vertex_index) {
	return vec3(chunk_normals[3u * vertex_index + 0u], chunk_normals[3u * vertex_index + 1u], chunk_normals[3u * vertex_index + 2u]);
}

vec2 fetch_chunk_uv(uint vertex_index) {
	return vec2(chunk_uvs[2u * vertex_index + 0u], chunk_uvs[2u * vertex_index + 1u]);
}

void main() {
	uint raw = uint(gl_InstanceCustomIndexEXT);
	bool is_proxy = (raw & PROXY_BIT) != 0u;
	uint inst = raw & (PROXY_BIT - 1u);
	uint tri = uint(gl_PrimitiveID);

	vec3 p0, p1, p2, na, nb, nc, albedo;
	vec3 emission = vec3(0.0);
	vec2 uv0 = vec2(0.0), uv1 = vec2(0.0), uv2 = vec2(0.0);
	uint texslot = 255u;
	float rough, metal, glassFlag;
	vec3 pp0 = vec3(0.0), pp1 = vec3(0.0), pp2 = vec3(0.0);
	bool has_prev = false;
	if (is_proxy) {
		uint vbase = chunk_offsets[3u * inst + 0u];
		uint ibase = chunk_offsets[3u * inst + 1u];
		texslot = chunk_offsets[3u * inst + 2u];
		uint l0 = chunk_indices[ibase + 3u * tri + 0u];
		uint l1 = chunk_indices[ibase + 3u * tri + 1u];
		uint l2 = chunk_indices[ibase + 3u * tri + 2u];
		p0 = fetch_chunk_position(vbase + l0);
		p1 = fetch_chunk_position(vbase + l1);
		p2 = fetch_chunk_position(vbase + l2);
		pp0 = fetch_chunk_position_prev(vbase + l0);
		pp1 = fetch_chunk_position_prev(vbase + l1);
		pp2 = fetch_chunk_position_prev(vbase + l2);
		has_prev = true;
		na = fetch_chunk_normal(vbase + l0);
		nb = fetch_chunk_normal(vbase + l1);
		nc = fetch_chunk_normal(vbase + l2);
		uv0 = fetch_chunk_uv(vbase + l0);
		uv1 = fetch_chunk_uv(vbase + l1);
		uv2 = fetch_chunk_uv(vbase + l2);
		albedo = vec3(chunk_colors[3u * inst + 0u], chunk_colors[3u * inst + 1u], chunk_colors[3u * inst + 2u]);
		rough = 1.0;
		metal = -1.0;
		glassFlag = 0.0;
	} else {
		uint vbase = offsets[2u * inst + 0u];
		uint ibase = offsets[2u * inst + 1u];
		uint l0 = indices[ibase + 3u * tri + 0u];
		uint l1 = indices[ibase + 3u * tri + 1u];
		uint l2 = indices[ibase + 3u * tri + 2u];
		p0 = fetch_position(vbase + l0);
		p1 = fetch_position(vbase + l1);
		p2 = fetch_position(vbase + l2);
		na = fetch_normal(vbase + l0);
		nb = fetch_normal(vbase + l1);
		nc = fetch_normal(vbase + l2);
		albedo = vec3(colors[3u * inst + 0u], colors[3u * inst + 1u], colors[3u * inst + 2u]);
		emission = vec3(emissions[3u * inst + 0u], emissions[3u * inst + 1u], emissions[3u * inst + 2u]);
		rough = materials[3u * inst + 0u];
		metal = materials[3u * inst + 1u];
		glassFlag = materials[3u * inst + 2u];
	}

	vec3 bary = vec3(1.0 - attribs.x - attribs.y, attribs.x, attribs.y);
	vec3 n_obj = na * bary.x + nb * bary.y + nc * bary.z;
	if (dot(n_obj, n_obj) < 1e-8) {
		n_obj = cross(p1 - p0, p2 - p0);
	}
	vec3 n_world = normalize(mat3(gl_ObjectToWorldEXT) * n_obj);
	if (dot(n_world, gl_WorldRayDirectionEXT) > 0.0) {
		n_world = -n_world;
	}

	vec2 uv = uv0 * bary.x + uv1 * bary.y + uv2 * bary.z;
	vec4 tex = vec4(1.0, 1.0, 1.0, 0.0);
	bool sampled = true;
	if (texslot == 0u) tex = textureLod(sampler2D(tex0, tex_sampler), uv, 0.0);
	else if (texslot == 1u) tex = textureLod(sampler2D(tex1, tex_sampler), uv, 0.0);
	else if (texslot == 2u) tex = textureLod(sampler2D(tex2, tex_sampler), uv, 0.0);
	else if (texslot == 3u) tex = textureLod(sampler2D(tex3, tex_sampler), uv, 0.0);
	else if (texslot == 4u) tex = textureLod(sampler2D(tex4, tex_sampler), uv, 0.0);
	else if (texslot == 5u) tex = textureLod(sampler2D(tex5, tex_sampler), uv, 0.0);
	else sampled = false;
	if (sampled) {
		albedo = mix(albedo, tex.rgb, tex.a);
	}

	if (!is_proxy) {
		float wlayer = world_tex_params[2u * inst + 0u];
		if (wlayer >= 0.0) {
			vec3 obj_scale = vec3(length(gl_ObjectToWorldEXT[0]), length(gl_ObjectToWorldEXT[1]), length(gl_ObjectToWorldEXT[2]));
			vec3 part_space = (p0 * bary.x + p1 * bary.y + p2 * bary.z) * obj_scale;
			vec2 wuv = part_uv(part_space, n_obj) / max(world_tex_params[2u * inst + 1u], 0.01);
			albedo *= textureLod(sampler2DArray(world_tex, world_sampler), vec3(wuv, wlayer), 0.0).rgb;
		}
	}

	payload.normal = n_world;
	payload.t = gl_HitTEXT;
	payload.roughness = rough;
	payload.metallic = metal;
	payload.color = albedo;
	payload.albedo = albedo;
	payload.glass = glassFlag;
	if (has_prev) {
		payload.prev_pos = pp0 * bary.x + pp1 * bary.y + pp2 * bary.z;
	} else {
		vec3 obj_pos = p0 * bary.x + p1 * bary.y + p2 * bary.z;
		payload.prev_pos = prev_world_pos(inst, obj_pos);
	}
	if (payload.secondary < 0.5) {
		return;
	}

	vec3 hit_pos = gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT;
	vec3 light_dir = normalize(cam.light_dir.xyz);
	float diff = max(dot(n_world, light_dir), 0.0);

	float shadow = 1.0;
	if (diff > 0.0 && payload.secondary < 2.5) {
		shadow_payload = 0.0;
		traceRayEXT(tlas,
			gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsSkipClosestHitShaderEXT | gl_RayFlagsOpaqueEXT,
			0x03, 0, 0, 1,
			hit_pos + n_world * 0.01, 0.01, light_dir, 10000.0, 1);
		shadow = shadow_payload;
	}

	float amb_strength = (payload.secondary < 1.5) ? AMBIENT_STRENGTH : GI_BOUNCE_AMBIENT;
	vec3 ambient = sky_ambient(n_world) * amb_strength;
	vec3 direct = cam.sun_color.rgb * cam.sun_color.w * (diff * shadow);
	payload.color = albedo * (ambient + direct) + emission;
	if (payload.secondary > 1.5 && payload.secondary < 2.5) {
		payload.color += albedo * sample_lights_gi(hit_pos, n_world);
	}
}
