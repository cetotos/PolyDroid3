using Godot;
using System.Collections.Generic;

namespace Polytoria.Client.Rendering;

public partial class RTCompositorTest : Node3D
{
	private Camera3D _camera = null!;
	private double _time;

	public override void _Ready()
	{
		GetViewport().Msaa3D = Viewport.Msaa.Disabled;

		Godot.Environment environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() }
		};

		WorldEnvironment worldEnvironment = new WorldEnvironment { Environment = environment };
		AddChild(worldEnvironment);

		DirectionalLight3D light = new DirectionalLight3D
		{
			RotationDegrees = new Vector3(-50, -40, 0),
			ShadowEnabled = true
		};
		AddChild(light);

		List<(MeshInstance3D Mesh, float Roughness, float Metallic, Vector3 Color)> meshes = new();

		MeshInstance3D ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20, 20) } };
		AddChild(ground);
		meshes.Add((ground, 0.1f, 0.0f, new Vector3(0.8f, 0.8f, 0.85f)));

		MeshInstance3D box = new MeshInstance3D { Mesh = new BoxMesh(), Position = new Vector3(-1.2f, 0.5f, 0) };
		AddChild(box);
		meshes.Add((box, 0.5f, 0.0f, new Vector3(0.8f, 0.2f, 0.2f)));

		MeshInstance3D sphere = new MeshInstance3D { Mesh = new SphereMesh(), Position = new Vector3(1.2f, 0.5f, 0) };
		AddChild(sphere);
		meshes.Add((sphere, 0.15f, 0.9f, new Vector3(0.9f, 0.85f, 0.4f)));

		RTReflectionEffect effect = new RTReflectionEffect();
		Compositor compositor = new Compositor
		{
			CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect }
		};

		_camera = new Camera3D { Position = new Vector3(0, 3, 6), Current = true, Compositor = compositor };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, 0.5f, 0), Vector3.Up);

		(RTShape[] shapes, RTInstance[] instances) = BuildScene(meshes);
		effect.SetScene(shapes, instances);
		effect.SetEnvironment(
			light.GlobalTransform.Basis.Column2,
			new Vector3(0.25f, 0.5f, 0.85f),
			new Vector3(0.6f, 0.78f, 0.92f),
			new Vector3(0.85f, 0.88f, 0.9f));
	}

	public override void _Process(double delta)
	{
		_time += delta;
		float radius = 6.0f;
		float angle = (float)_time * 0.3f;
		_camera.Position = new Vector3(Mathf.Sin(angle) * radius, 3.0f, Mathf.Cos(angle) * radius);
		_camera.LookAt(new Vector3(0, 0.5f, 0), Vector3.Up);
	}

	private static (RTShape[] Shapes, RTInstance[] Instances) BuildScene(List<(MeshInstance3D Mesh, float Roughness, float Metallic, Vector3 Color)> meshInstances)
	{
		List<RTShape> shapes = new();
		List<RTInstance> instances = new();

		foreach ((MeshInstance3D meshInstance, float roughness, float metallic, Vector3 color) in meshInstances)
		{
			Mesh? mesh = meshInstance.Mesh;
			if (mesh == null || mesh.GetSurfaceCount() == 0)
			{
				continue;
			}

			List<float> positions = new();
			List<float> normals = new();
			List<int> indices = new();
			int vbase = 0;

			for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
			{
				Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
				Vector3[] verts = arrays[(int)Mesh.ArrayType.Vertex].As<Vector3[]>();
				if (verts == null || verts.Length == 0)
				{
					continue;
				}

				Vector3[] norms = arrays[(int)Mesh.ArrayType.Normal].As<Vector3[]>();
				int[] idx = arrays[(int)Mesh.ArrayType.Index].As<int[]>();
				if (idx == null || idx.Length == 0)
				{
					idx = new int[verts.Length];
					for (int i = 0; i < verts.Length; i++)
					{
						idx[i] = i;
					}
				}

				for (int i = 0; i < verts.Length; i++)
				{
					positions.Add(verts[i].X);
					positions.Add(verts[i].Y);
					positions.Add(verts[i].Z);

					Vector3 nrm = (norms != null && i < norms.Length) ? norms[i] : Vector3.Zero;
					normals.Add(nrm.X);
					normals.Add(nrm.Y);
					normals.Add(nrm.Z);
				}

				foreach (int index in idx)
				{
					indices.Add(vbase + index);
				}

				vbase += verts.Length;
			}

			int shapeIndex = shapes.Count;
			shapes.Add(new RTShape
			{
				Positions = positions.ToArray(),
				Normals = normals.ToArray(),
				Indices = indices.ToArray()
			});

			instances.Add(new RTInstance
			{
				ShapeIndex = shapeIndex,
				Transform = meshInstance.GlobalTransform,
				Roughness = roughness,
				Metallic = metallic,
				Color = color
			});
		}

		return (shapes.ToArray(), instances.ToArray());
	}
}
