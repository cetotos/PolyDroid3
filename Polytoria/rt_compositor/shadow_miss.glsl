#[miss]

#version 460

#pragma shader_stage(miss)
#extension GL_EXT_ray_tracing : enable

layout(location = 1) rayPayloadInEXT float shadow_payload;

void main() {
	shadow_payload = 1.0;
}
