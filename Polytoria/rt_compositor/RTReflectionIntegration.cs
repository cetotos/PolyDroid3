using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.Rendering;

public static class RTReflectionIntegration
{
	public static void Install(World root)
	{
		if (Globals.IsMobileBuild)
		{
			return;
		}

		string method = RenderingServer.GetCurrentRenderingMethod();
		string driver = RenderingServer.GetCurrentRenderingDriverName();
		if (method != "forward_plus")
		{
			return;
		}
		if (!driver.Equals("vulkan", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		root.Loaded.Once(() => BuildAndAttach(root));
	}

	private static void BuildAndAttach(World root)
	{
		Camera3D? camera = root.Environment?.CurrentCamera?.Camera3D;
		if (camera == null)
		{
			return;
		}

		(RTShape[] shapes, RTInstance[] instances, List<(int, Part)> dynamicParts, float[] lights, Image[] worldTextures) = GatherScene(root);
		if (instances.Length == 0)
		{
			return;
		}

		RTReflectionEffect effect = new RTReflectionEffect();
		effect.SetWorldTextures(worldTextures);
		effect.SetScene(shapes, instances);
		effect.SetLights(lights);

		(Vector3 skyTop, Vector3 skyBottom, Vector3 skyHorizon) = GetSkyColors(root);
		effect.SetEnvironment(GetSunDirection(root), skyTop, skyBottom, skyHorizon);
		(Vector3 sunColor, float sunIntensity) = GetSunColor(root);
		effect.SetSun(sunColor, sunIntensity);

		camera.Compositor = new Compositor
		{
			CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect }
		};

		camera.GetViewport().Msaa3D = Viewport.Msaa.Disabled;

		RTReflectionManager manager = new RTReflectionManager();
		manager.Setup(root, effect);
		manager.SetDynamicParts(dynamicParts);
		root.GDNode.AddChild(manager);
	}

	private static Vector3 GetSunDirection(World root)
	{
		try
		{
			SunLight sun = root.Lighting.Sun;
			if (sun.GDLight != null && sun.GDLight.IsInsideTree())
			{
				Vector3 toLight = sun.GDLight.GlobalTransform.Basis.Column2;
				if (toLight.LengthSquared() > 0.0001f)
				{
					return toLight.Normalized();
				}
			}
		}
		catch (System.Exception e)
		{
			GD.PushError("RTReflectionIntegration: failed to read sun direction: " + e.Message);
		}
		return new Vector3(0.4f, 1.0f, 0.3f).Normalized();
	}

	private static (Vector3 Color, float Intensity) GetSunColor(World root)
	{
		try
		{
			if (root.Lighting.Sun?.GDLight is DirectionalLight3D sun)
			{
				Godot.Color c = sun.LightColor.SrgbToLinear();
				float energy = Mathf.Max(0.1f, sun.LightEnergy);
				return (new Vector3(c.R, c.G, c.B), energy);
			}
		}
		catch (System.Exception e)
		{
			GD.PushError("RTReflectionIntegration: failed to read sun color: " + e.Message);
		}
		return (new Vector3(1.0f, 0.96f, 0.9f), 1.5f);
	}

	private static (Vector3 Top, Vector3 Bottom, Vector3 Horizon) GetSkyColors(World root)
	{
		Vector3 top = new(0.25f, 0.52f, 0.84f);
		Vector3 bottom = new(0.60f, 0.79f, 0.92f);
		Vector3 horizon = new(0.79f, 0.87f, 0.91f);
		try
		{
			if (root.Lighting.environment?.Sky?.SkyMaterial is ShaderMaterial skyMat)
			{
				top = ReadColorParam(skyMat, "sky_gradient_top", top);
				bottom = ReadColorParam(skyMat, "sky_gradient_bottom", bottom);
				horizon = ReadColorParam(skyMat, "horizon_line_color", horizon);
			}
		}
		catch (System.Exception e)
		{
			GD.PushError("RTReflectionIntegration: failed to read sky colors: " + e.Message);
		}
		return (top, bottom, horizon);
	}

	private static Vector3 ReadColorParam(ShaderMaterial material, string name, Vector3 fallback)
	{
		Variant value = material.GetShaderParameter(name);
		if (value.VariantType == Variant.Type.Color)
		{
			Godot.Color c = value.As<Godot.Color>().SrgbToLinear();
			return new Vector3(c.R, c.G, c.B);
		}
		return fallback;
	}

	private const int WorldTexSize = 256;

	private static (RTShape[] Shapes, RTInstance[] Instances, List<(int, Part)> DynamicParts, float[] Lights, Image[] WorldTextures) GatherScene(World root)
	{
		Dictionary<Part.ShapeEnum, int> shapeIndex = new();
		List<RTShape> shapes = new();
		List<RTInstance> instances = new();
		List<(int, Part)> dynamicParts = new();
		List<(Vector3 Pos, Vector3 Color, float Range, float Intensity)> lights = new();
		Dictionary<Part.PartMaterialEnum, int> texLayerIndex = new();
		List<Image> worldTexImages = new();
		List<float> worldTexStuds = new();

		foreach (var item in root.Environment.GetDescendants())
		{
			if (item is not Part part)
			{
				continue;
			}
			if (part.IsHidden || part.Color.A < 0.05f)
			{
				continue;
			}

			if (!shapeIndex.TryGetValue(part.Shape, out int si))
			{
				RTShape? shape = LoadShapeGeometry(part.Shape);
				if (shape == null)
				{
					continue;
				}
				si = shapes.Count;
				shapeIndex[part.Shape] = si;
				shapes.Add(shape);
			}

			(float roughness, float metallic, float emissive) = GetMaterialParams(part.Material, part.Color.A);
			if (!texLayerIndex.TryGetValue(part.Material, out int texLayer))
			{
				(Image? albedoImage, float studs) = LoadMaterialAlbedo(part.Material);
				texLayer = -1;
				if (albedoImage != null)
				{
					texLayer = worldTexImages.Count;
					worldTexImages.Add(albedoImage);
					worldTexStuds.Add(studs);
				}
				texLayerIndex[part.Material] = texLayer;
			}
			Godot.Color linear = part.Color.SrgbToLinear();
			Vector3 albedoLinear = new Vector3(linear.R, linear.G, linear.B);
			if (emissive > 0.05f)
			{
				Vector3 lpos = part.GetGlobalTransform().Origin;
				float lrange = Mathf.Clamp(12f + emissive * 8f, 12f, 55f);
				lights.Add((lpos, albedoLinear, lrange, emissive * 3.5f));
			}
			if (!part.Anchored)
			{
				dynamicParts.Add((instances.Count, part));
			}
			instances.Add(new RTInstance
			{
				ShapeIndex = si,
				Transform = part.GetGlobalTransform(),
				Roughness = roughness,
				Metallic = metallic,
				Color = albedoLinear,
				Emission = albedoLinear * emissive,
				Transparent = part.Color.A < 0.98f,
				TexLayer = texLayer,
				StudsPerTile = texLayer >= 0 ? worldTexStuds[texLayer] : 2f
			});
		}

		const int maxLights = 16;
		if (lights.Count > maxLights)
		{
			lights.Sort((a, b) => b.Intensity.CompareTo(a.Intensity));
			lights = lights.GetRange(0, maxLights);
		}
		float[] packed = new float[1 + lights.Count * 8];
		packed[0] = lights.Count;
		for (int i = 0; i < lights.Count; i++)
		{
			int b = 1 + i * 8;
			packed[b] = lights[i].Pos.X;
			packed[b + 1] = lights[i].Pos.Y;
			packed[b + 2] = lights[i].Pos.Z;
			packed[b + 3] = lights[i].Range;
			packed[b + 4] = lights[i].Color.X;
			packed[b + 5] = lights[i].Color.Y;
			packed[b + 6] = lights[i].Color.Z;
			packed[b + 7] = lights[i].Intensity;
		}

		return (shapes.ToArray(), instances.ToArray(), dynamicParts, packed, worldTexImages.ToArray());
	}

	private static (Image? Image, float Studs) LoadMaterialAlbedo(Part.PartMaterialEnum material)
	{
		try
		{
			if (Globals.LoadMaterial(material, 1f) is not ShaderMaterial sm)
			{
				return (null, 2f);
			}
			Variant useTex = sm.GetShaderParameter("use_albedo_texture");
			if (useTex.VariantType != Variant.Type.Bool || !useTex.As<bool>())
			{
				return (null, 2f);
			}
			if (sm.GetShaderParameter("albedo").As<Texture2D>() is not Texture2D tex)
			{
				return (null, 2f);
			}
			Image? img = tex.GetImage();
			if (img == null || img.IsEmpty())
			{
				return (null, 2f);
			}
			if (img.IsCompressed())
			{
				img.Decompress();
			}
			if (img.HasMipmaps())
			{
				img.ClearMipmaps();
			}
			img.Convert(Image.Format.Rgba8);
			img.Resize(WorldTexSize, WorldTexSize, Image.Interpolation.Lanczos);
			if (img.HasMipmaps())
			{
				img.ClearMipmaps();
			}
			return (img, ReadFloatParam(sm, "studs_per_tile", 2f));
		}
		catch (System.Exception e)
		{
			GD.PushError("Failed to load albedo for " + material + ": " + e.Message);
			return (null, 2f);
		}
	}

	private static RTShape? LoadShapeGeometry(Part.ShapeEnum shape)
	{
		(Godot.Mesh mesh, _) = Globals.LoadShape(shape.ToString());
		if (mesh == null || mesh.GetSurfaceCount() == 0)
		{
			return null;
		}

		List<float> positions = new();
		List<float> normals = new();
		List<int> indices = new();
		int vbase = 0;

		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
			Vector3[] verts = arrays[(int)Godot.Mesh.ArrayType.Vertex].As<Vector3[]>();
			if (verts == null || verts.Length == 0)
			{
				continue;
			}

			Vector3[] norms = arrays[(int)Godot.Mesh.ArrayType.Normal].As<Vector3[]>();
			int[] idx = arrays[(int)Godot.Mesh.ArrayType.Index].As<int[]>();
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

		if (positions.Count == 0 || indices.Count == 0)
		{
			return null;
		}

		return new RTShape
		{
			Positions = positions.ToArray(),
			Normals = normals.ToArray(),
			Indices = indices.ToArray()
		};
	}

	private static (float Roughness, float Metallic, float Emissive) GetMaterialParams(Part.PartMaterialEnum material, float alpha)
	{
		Godot.Material mat = Globals.LoadMaterial(material, alpha);
		if (mat is ShaderMaterial sm)
		{
			return (ReadFloatParam(sm, "roughness", 0.7f), ReadFloatParam(sm, "metallic", 0.0f), ReadFloatParam(sm, "emissive_strength", 0.0f));
		}
		return (0.7f, 0.0f, 0.0f);
	}

	private static float ReadFloatParam(ShaderMaterial material, string name, float fallback)
	{
		Variant value = material.GetShaderParameter(name);
		return value.VariantType == Variant.Type.Float ? (float)value : fallback;
	}
}
