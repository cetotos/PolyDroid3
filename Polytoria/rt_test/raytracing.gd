extends Node3D

@onready
var rd := RenderingServer.create_local_rendering_device()
@onready
var screen_texture := get_node("TextureRect")

var raytracing_texture: RID
var shader: RID
var shadow_shader: RID
var sbt: RID
var sbt_range: int
var raytracing_pipeline: RID
var tlas: RID
var camera_buffer: RID
var positions_buffer: RID
var indices_buffer: RID
var offsets_buffer: RID
var normals_buffer: RID
var uniform_set: RID

@export var render_scale := 1.0

var _owned_rids: Array = []
var _time := 0.0
var _width := 0
var _height := 0

func _free_rid(p_rd: RenderingDevice, rid: RID):
	if rid != null:
		p_rd.free_rid(rid)

func _cleanup():
	if rd == null:
		return

	_free_rid(rd, uniform_set)
	_free_rid(rd, normals_buffer)
	_free_rid(rd, offsets_buffer)
	_free_rid(rd, indices_buffer)
	_free_rid(rd, positions_buffer)
	_free_rid(rd, camera_buffer)
	_free_rid(rd, tlas)
	for rid in _owned_rids:
		_free_rid(rd, rid)
	_owned_rids.clear()
	rd.hit_sbt_range_free(sbt, sbt_range)
	_free_rid(rd, sbt)
	_free_rid(rd, raytracing_pipeline)
	_free_rid(rd, shadow_shader)
	_free_rid(rd, shader)
	_free_rid(rd, raytracing_texture)
	rd = null

func _notification(what: int):
	if what == NOTIFICATION_PREDELETE:
		_cleanup()

func _ready():
	if not rd.has_feature(RenderingDevice.SUPPORTS_RAYTRACING_PIPELINE):
		push_error("Ray tracing pipeline NOT supported on this device/driver")
		return

	screen_texture.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	screen_texture.stretch_mode = TextureRect.STRETCH_SCALE

	_build_test_scene()
	_initialize_raytracing_pipeline()
	_initialize_scene()
	_initialize_camera()

func _process(delta):
	if rd != null and rd.has_feature(RenderingDevice.SUPPORTS_RAYTRACING_PIPELINE):
		_time += delta
		_render()

func _build_test_scene():
	var ground := MeshInstance3D.new()
	var ground_mesh := PlaneMesh.new()
	ground_mesh.size = Vector2(30, 30)
	ground.mesh = ground_mesh
	add_child(ground)
	ground.position = Vector3(0, -1, 3)

	var box := MeshInstance3D.new()
	box.mesh = BoxMesh.new()
	add_child(box)
	box.position = Vector3(-1.2, 0, 3)

	var sphere := MeshInstance3D.new()
	sphere.mesh = SphereMesh.new()
	add_child(sphere)
	sphere.position = Vector3(1.2, 0, 4)

func _collect_mesh_instances(node: Node, out: Array) -> void:
	for child in node.get_children():
		if child is MeshInstance3D:
			out.append(child)
		_collect_mesh_instances(child, out)

func _initialize_raytracing_texture():
	var texture_format := RDTextureFormat.new()
	texture_format.texture_type = RenderingDevice.TEXTURE_TYPE_2D
	texture_format.format = RenderingDevice.DATA_FORMAT_R8G8B8A8_UNORM
	texture_format.width = _width
	texture_format.height = _height
	texture_format.usage_bits = RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT | RenderingDevice.TEXTURE_USAGE_STORAGE_BIT
	var texture_view := RDTextureView.new()
	raytracing_texture = rd.texture_create(texture_format, texture_view)

func _initialise_screen_texture():
	var image = Image.create(_width, _height, false, Image.FORMAT_RGBA8)
	var image_texture = ImageTexture.create_from_image(image)
	screen_texture.texture = image_texture

func _set_screen_texture_data(data: PackedByteArray):
	var image := Image.create_from_data(_width, _height, false, Image.FORMAT_RGBA8, data)
	screen_texture.texture.update(image)

func _initialize_camera():
	var zero := PackedByteArray()
	zero.resize(64)
	camera_buffer = rd.uniform_buffer_create(64, zero)

func _update_camera():
	var target := Vector3(0.0, 0.0, 3.0)
	var radius := 6.0
	var origin := target + Vector3(sin(_time) * radius, 2.0, cos(_time) * radius)
	var forward := (target - origin).normalized()
	var right := forward.cross(Vector3.UP).normalized()
	var up := right.cross(forward).normalized()

	var fov := deg_to_rad(70.0)
	var tan_half := tan(fov * 0.5)
	var aspect := float(_width) / float(_height)
	right *= tan_half * aspect
	up *= tan_half

	var data := PackedFloat32Array([
		origin.x, origin.y, origin.z, 0.0,
		right.x, right.y, right.z, 0.0,
		up.x, up.y, up.z, 0.0,
		forward.x, forward.y, forward.z, 0.0,
	])
	var bytes := data.to_byte_array()
	rd.buffer_update(camera_buffer, 0, bytes.size(), bytes)

func _initialize_raytracing_pipeline():
	var shader_file := load("res://rt_test/ray.glsl")
	var shader_spirv: RDShaderSPIRV = shader_file.get_spirv()
	shader = rd.shader_create_from_spirv(shader_spirv)

	var shadow_file := load("res://rt_test/shadow_miss.glsl")
	var shadow_spirv: RDShaderSPIRV = shadow_file.get_spirv()
	shadow_shader = rd.shader_create_from_spirv(shadow_spirv)

	var pipeline_shader = RDPipelineShader.new()
	pipeline_shader.shader = shader

	var shadow_pipeline_shader = RDPipelineShader.new()
	shadow_pipeline_shader.shader = shadow_shader

	var hit_group = RDHitGroup.new()
	hit_group.closest_hit_shader = pipeline_shader

	raytracing_pipeline = rd.raytracing_pipeline_create([pipeline_shader], [pipeline_shader, shadow_pipeline_shader], [hit_group], 2)

	sbt = rd.hit_sbt_create(raytracing_pipeline, 1024)
	sbt_range = rd.hit_sbt_range_alloc(sbt, 1)
	assert(sbt_range != 0)
	var err = rd.hit_sbt_range_update(sbt, sbt_range, 0, [0])
	assert(err == OK)

func _create_uniforms():
	var image_uniform := RDUniform.new()
	image_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_IMAGE
	image_uniform.binding = 0
	image_uniform.add_id(raytracing_texture)

	var as_uniform := RDUniform.new()
	as_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_ACCELERATION_STRUCTURE
	as_uniform.binding = 1
	as_uniform.add_id(tlas)

	var camera_uniform := RDUniform.new()
	camera_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_UNIFORM_BUFFER
	camera_uniform.binding = 2
	camera_uniform.add_id(camera_buffer)

	var positions_uniform := RDUniform.new()
	positions_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	positions_uniform.binding = 3
	positions_uniform.add_id(positions_buffer)

	var indices_uniform := RDUniform.new()
	indices_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	indices_uniform.binding = 4
	indices_uniform.add_id(indices_buffer)

	var offsets_uniform := RDUniform.new()
	offsets_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	offsets_uniform.binding = 5
	offsets_uniform.add_id(offsets_buffer)

	var normals_uniform := RDUniform.new()
	normals_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
	normals_uniform.binding = 6
	normals_uniform.add_id(normals_buffer)

	uniform_set = rd.uniform_set_create([image_uniform, as_uniform, camera_uniform, positions_uniform, indices_uniform, offsets_uniform, normals_uniform], shader, 0)

func _initialize_scene():
	var mesh_instances: Array = []
	_collect_mesh_instances(self, mesh_instances)

	var all_positions := PackedFloat32Array()
	var all_normals := PackedFloat32Array()
	var all_indices := PackedInt32Array()
	var offsets := PackedInt32Array()
	var instances: Array = []
	var id := 0
	var vcount := 0
	var icount := 0

	for mi in mesh_instances:
		var mesh: Mesh = mi.mesh
		if mesh == null:
			continue
		for s in range(mesh.get_surface_count()):
			var arrays := mesh.surface_get_arrays(s)
			if arrays[Mesh.ARRAY_VERTEX] == null:
				continue
			var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
			if verts.is_empty():
				continue

			var norms := PackedVector3Array()
			if arrays[Mesh.ARRAY_NORMAL] != null:
				norms = arrays[Mesh.ARRAY_NORMAL]

			var indices := PackedInt32Array()
			if arrays[Mesh.ARRAY_INDEX] != null:
				indices = arrays[Mesh.ARRAY_INDEX]
			if indices.is_empty():
				for i in range(verts.size()):
					indices.append(i)

			var vbytes := verts.to_byte_array()
			var vbuf := rd.vertex_buffer_create(vbytes.size(), vbytes, RenderingDevice.BUFFER_CREATION_DEVICE_ADDRESS_BIT | RenderingDevice.BUFFER_CREATION_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT)
			_owned_rids.append(vbuf)

			var ibytes := indices.to_byte_array()
			var ibuf := rd.index_buffer_create(indices.size(), RenderingDevice.INDEX_BUFFER_FORMAT_UINT32, ibytes, false, RenderingDevice.BUFFER_CREATION_DEVICE_ADDRESS_BIT | RenderingDevice.BUFFER_CREATION_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT)
			_owned_rids.append(ibuf)

			var geom := RDAccelerationStructureGeometry.new()
			geom.vertex_buffer = vbuf
			geom.vertex_count = verts.size()
			geom.vertex_format = RenderingDevice.DATA_FORMAT_R32G32B32_SFLOAT
			geom.vertex_stride = 4 * 3
			geom.index_buffer = ibuf
			geom.index_count = indices.size()

			var mesh_blas := rd.blas_create([geom], RenderingDevice.ACCELERATION_STRUCTURE_GEOMETRY_OPAQUE_BIT)
			rd.blas_build(mesh_blas)
			_owned_rids.append(mesh_blas)

			offsets.append(vcount)
			offsets.append(icount)
			for vi in range(verts.size()):
				var v: Vector3 = verts[vi]
				all_positions.append(v.x)
				all_positions.append(v.y)
				all_positions.append(v.z)
				var n := Vector3.ZERO
				if vi < norms.size():
					n = norms[vi]
				all_normals.append(n.x)
				all_normals.append(n.y)
				all_normals.append(n.z)
			for ix in indices:
				all_indices.append(ix)
			vcount += verts.size()
			icount += indices.size()

			var instance := RDAccelerationStructureInstance.new()
			instance.blas = mesh_blas
			instance.transform = mi.global_transform
			instance.hit_sbt_range = sbt_range
			instance.mask = 0xFF
			instance.id = id
			instances.append(instance)
			id += 1

	if instances.is_empty():
		push_error("RT: no mesh geometry collected to build acceleration structures")
		return

	var pos_bytes := all_positions.to_byte_array()
	positions_buffer = rd.storage_buffer_create(pos_bytes.size(), pos_bytes)
	var idx_bytes := all_indices.to_byte_array()
	indices_buffer = rd.storage_buffer_create(idx_bytes.size(), idx_bytes)
	var off_bytes := offsets.to_byte_array()
	offsets_buffer = rd.storage_buffer_create(off_bytes.size(), off_bytes)
	var nrm_bytes := all_normals.to_byte_array()
	normals_buffer = rd.storage_buffer_create(nrm_bytes.size(), nrm_bytes)

	tlas = rd.tlas_create(instances.size(), RenderingDevice.ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT)
	rd.tlas_build(tlas, instances)

func _ensure_size() -> bool:
	var vp = get_viewport().size
	var scale = clampf(render_scale, 0.1, 1.0)
	var target_w = maxi(1, int(vp.x * scale))
	var target_h = maxi(1, int(vp.y * scale))
	if raytracing_texture.is_valid() and target_w == _width and target_h == _height:
		return true
	if vp.x <= 0 or vp.y <= 0:
		return false
	if uniform_set.is_valid():
		rd.free_rid(uniform_set)
	if raytracing_texture.is_valid():
		rd.free_rid(raytracing_texture)
	_width = target_w
	_height = target_h
	_initialize_raytracing_texture()
	_initialise_screen_texture()
	_create_uniforms()
	return true

func _render():
	if not _ensure_size():
		return
	_update_camera()

	var raylist = rd.raytracing_list_begin()
	rd.raytracing_list_bind_raytracing_pipeline(raylist, raytracing_pipeline)
	rd.raytracing_list_bind_uniform_set(raylist, uniform_set, 0)
	rd.raytracing_list_trace_rays(raylist, 0, sbt, _width, _height, 1)
	rd.raytracing_list_end()

	var byte_data := rd.texture_get_data(raytracing_texture, 0)
	_set_screen_texture_data(byte_data)
