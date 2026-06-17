#[raygen]

#version 460

#pragma shader_stage(raygen)
#extension GL_EXT_ray_tracing : enable

layout(location = 0) rayPayloadEXT vec3 payload;

layout(set = 0, binding = 0, rgba8) uniform image2D image;
layout(set = 0, binding = 1) uniform accelerationStructureEXT tlas;

layout(set = 0, binding = 2, std140) uniform Camera {
	vec4 origin;
	vec4 right;
	vec4 up;
	vec4 forward;
} cam;

void main() {
	const vec2 pixel_center = vec2(gl_LaunchIDEXT.xy) + vec2(0.5);
	const vec2 uv = pixel_center / vec2(gl_LaunchSizeEXT.xy);
	vec2 ndc = vec2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);

	vec3 origin = cam.origin.xyz;
	vec3 direction = normalize(cam.forward.xyz + ndc.x * cam.right.xyz + ndc.y * cam.up.xyz);

	float t_min = 0.001;
	float t_max = 10000.0;

	traceRayEXT(tlas, gl_RayFlagsOpaqueEXT, 0xFF, 0, 0, 0, origin, t_min, direction, t_max, 0);

	imageStore(image, ivec2(gl_LaunchIDEXT.xy), vec4(payload, 1.0));
}

#[miss]

#version 460
#extension GL_EXT_ray_tracing : enable

layout(location = 0) rayPayloadInEXT vec3 payload;

void main() {
	float a = 0.5 * (normalize(gl_WorldRayDirectionEXT).y + 1.0);
	payload = mix(vec3(1.0, 1.0, 1.0), vec3(0.4, 0.6, 1.0), a);
}

#[closest_hit]

#version 460

#pragma shader_stage(closest_hit)
#extension GL_EXT_ray_tracing : enable

layout(location = 0) rayPayloadInEXT vec3 payload;
layout(location = 1) rayPayloadEXT float shadow_payload;
hitAttributeEXT vec2 attribs;

layout(set = 0, binding = 1) uniform accelerationStructureEXT tlas;
layout(set = 0, binding = 3, std430) readonly buffer Positions { float positions[]; };
layout(set = 0, binding = 4, std430) readonly buffer Indices { uint indices[]; };
layout(set = 0, binding = 5, std430) readonly buffer Offsets { uint offsets[]; };
layout(set = 0, binding = 6, std430) readonly buffer Normals { float normals[]; };

vec3 fetch_position(uint vertex_index) {
	return vec3(positions[3u * vertex_index + 0u], positions[3u * vertex_index + 1u], positions[3u * vertex_index + 2u]);
}

vec3 fetch_normal(uint vertex_index) {
	return vec3(normals[3u * vertex_index + 0u], normals[3u * vertex_index + 1u], normals[3u * vertex_index + 2u]);
}

void main() {
	uint inst = uint(gl_InstanceCustomIndexEXT);
	uint vbase = offsets[2u * inst + 0u];
	uint ibase = offsets[2u * inst + 1u];
	uint tri = uint(gl_PrimitiveID);

	uint l0 = indices[ibase + 3u * tri + 0u];
	uint l1 = indices[ibase + 3u * tri + 1u];
	uint l2 = indices[ibase + 3u * tri + 2u];

	vec3 p0 = fetch_position(vbase + l0);
	vec3 p1 = fetch_position(vbase + l1);
	vec3 p2 = fetch_position(vbase + l2);

	vec3 bary = vec3(1.0 - attribs.x - attribs.y, attribs.x, attribs.y);
	vec3 n_obj = fetch_normal(vbase + l0) * bary.x + fetch_normal(vbase + l1) * bary.y + fetch_normal(vbase + l2) * bary.z;
	if (dot(n_obj, n_obj) < 1e-8) {
		n_obj = cross(p1 - p0, p2 - p0);
	}
	vec3 n_world = normalize(mat3(gl_ObjectToWorldEXT) * n_obj);
	if (dot(n_world, gl_WorldRayDirectionEXT) > 0.0) {
		n_world = -n_world;
	}

	vec3 light_dir = normalize(vec3(0.4, 1.0, 0.3));
	float diff = max(dot(n_world, light_dir), 0.0);

	float shadow = 1.0;
	if (diff > 0.0) {
		vec3 hit_pos = gl_WorldRayOriginEXT + gl_WorldRayDirectionEXT * gl_HitTEXT;
		shadow_payload = 0.0;
		traceRayEXT(tlas,
			gl_RayFlagsTerminateOnFirstHitEXT | gl_RayFlagsSkipClosestHitShaderEXT | gl_RayFlagsOpaqueEXT,
			0xFF, 0, 0, 1,
			hit_pos + n_world * 0.002, 0.001, light_dir, 10000.0, 1);
		shadow = shadow_payload;
	}

	float lit = diff * shadow;
	payload = vec3(0.12) + vec3(0.9, 0.88, 0.82) * lit;
}
