using Godot;
using Polytoria.Datamodel;
using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.Rendering;

public partial class RTReflectionManager : Node
{
	private const int MaxParts = 64;
	private const int TexSlots = 6;
	private const int NoTexture = 255;
	private const float FirstPersonDistance = 2.5f;
	private static readonly Vector3 ParkPosition = new(0f, -100000f, 0f);

	private World _world = null!;
	private RTReflectionEffect _effect = null!;

	private readonly List<SkinPart> _parts = new();
	private ulong[] _trackedMeshes = Array.Empty<ulong>();
	private int _totalVerts;
	private bool _topologyReady;
	private bool _proxiesParked;
	private readonly MeshInstance3D?[] _texSources = new MeshInstance3D?[TexSlots];

	private List<(int, Part)>? _dynamicParts;
	private Transform3D[]? _lastDynTransforms;
	private float[]? _prevPositions;

	public void Setup(World world, RTReflectionEffect effect)
	{
		_world = world;
		_effect = effect;
	}

	public void SetDynamicParts(List<(int, Part)> parts)
	{
		_dynamicParts = parts;
		_lastDynTransforms = new Transform3D[parts.Count];
	}

	private void UpdateDynamicParts()
	{
		if (_dynamicParts == null || _dynamicParts.Count == 0 || _lastDynTransforms == null)
		{
			return;
		}
		List<int> indices = new();
		List<Transform3D> transforms = new();
		for (int i = 0; i < _dynamicParts.Count; i++)
		{
			Transform3D cur;
			try
			{
				cur = _dynamicParts[i].Item2.GetGlobalTransform();
			}
			catch (System.Exception)
			{
				continue;
			}
			if (!cur.IsEqualApprox(_lastDynTransforms[i]))
			{
				_lastDynTransforms[i] = cur;
				indices.Add(_dynamicParts[i].Item1);
				transforms.Add(cur);
			}
		}
		if (indices.Count > 0)
		{
			_effect.UpdateDynamicParts(indices.ToArray(), transforms.ToArray());
		}
	}

	public override void _Process(double delta)
	{
		if (_effect == null)
		{
			return;
		}

		bool rtEnabled = ClientSettingsService.Instance != null && ClientSettingsService.Instance.Get<bool>(SharedSettingKeys.PostProcessing.RtReflections);
		_effect.Enabled = rtEnabled;
		ManageSdfgi(rtEnabled);
		ManageCinematicPost(rtEnabled);
		ManageAmbient(rtEnabled && ClientSettingsService.Instance!.Get<bool>(SharedSettingKeys.RayTracing.GlobalIllumination));
		if (!rtEnabled)
		{
			return;
		}

		var settings = ClientSettingsService.Instance!;
		_effect.SetRtParams(
			settings.Get<float>(SharedSettingKeys.RayTracing.GiStrength),
			settings.Get<bool>(SharedSettingKeys.RayTracing.Volumetrics) ? 0.045f : 0f,
			settings.Get<bool>(SharedSettingKeys.RayTracing.GlobalIllumination) ? 4 : 0,
			settings.Get<bool>(SharedSettingKeys.RayTracing.Reflections) ? 8 : 0);

		UpdateDynamicParts();

		List<MeshEntry> meshes = CollectMeshes();
		if (MeshSetChanged(meshes))
		{
			Regather(meshes);
		}

		if (!_topologyReady || _totalVerts == 0)
		{
			if (!_proxiesParked)
			{
				_effect.ParkProxies();
				_proxiesParked = true;
			}
			return;
		}
		_proxiesParked = false;

		float[] positions = new float[_totalVerts * 3];
		float[] normals = new float[_totalVerts * 3];
		SkinFrame(positions, normals);
		float[] prevPositions = (_prevPositions != null && _prevPositions.Length == positions.Length) ? _prevPositions : positions;
		_effect.UpdateAvatar(positions, normals, ReadColors(), prevPositions);
		_prevPositions = positions;
		_effect.SetAvatarTextures(FetchTextures());
	}

	private List<MeshEntry> CollectMeshes()
	{
		List<MeshEntry> result = new();
		Players? players = _world?.Players;
		if (players == null)
		{
			return result;
		}
		Player? localPlayer = players.LocalPlayer;
		bool firstPerson = IsFirstPerson();
		bool firstAvatar = true;
		foreach (var item in players.GetDescendants())
		{
			if (item is not Player player)
			{
				continue;
			}
			if (firstPerson && player == localPlayer)
			{
				continue;
			}
			Node? root = player.Character?.GDNode;
			if (root == null)
			{
				continue;
			}
			Skeleton3D? skeleton = root.GetNodeOrNull<Skeleton3D>("Character/Poly/Skeleton3D");
			if (skeleton == null || !skeleton.IsInsideTree())
			{
				continue;
			}
			CollectRecursive(root, skeleton, firstAvatar, result);
			firstAvatar = false;
		}
		return result;
	}

	private bool _sdfgiState = true;

	private void ManageSdfgi(bool rtEnabled)
	{
		bool want = !rtEnabled;
		if (want == _sdfgiState)
		{
			return;
		}
		try
		{
			Godot.Environment? env = _world.Lighting.environment;
			if (env != null)
			{
				env.SdfgiEnabled = want;
				_sdfgiState = want;
			}
		}
		catch (System.Exception)
		{
		}
	}

	private bool _ambientTakenOver;

	private void ManageAmbient(bool giActive)
	{
		if (giActive == _ambientTakenOver)
		{
			return;
		}
		try
		{
			Godot.Environment? env = _world.Lighting.environment;
			if (env == null)
			{
				return;
			}
			if (_postApplied)
			{
				env.AmbientLightEnergy = giActive ? 0.05f : 0.15f;
			}
			_ambientTakenOver = giActive;
		}
		catch (System.Exception)
		{
		}
	}

	private bool _postApplied;
	private bool _postCaptured;
	private Godot.Environment.ToneMapper _origTonemap;
	private float _origExposure;
	private bool _origGlow;
	private float _origGlowBloom;
	private float _origGlowIntensity;
	private float _origGlowStrength;
	private float _origGlowHdrThreshold;
	private bool _origAdjustEnabled;
	private float _origAdjustBrightness;
	private float _origAdjustContrast;
	private float _origAdjustSaturation;
	private float _origSunAngular;
	private float _origSunBlur;
	private Godot.Environment.AmbientSource _origAmbientSource;
	private float _origAmbientEnergy;

	private void ManageCinematicPost(bool rtEnabled)
	{
		if (rtEnabled == _postApplied)
		{
			return;
		}
		try
		{
			Godot.Environment? env = _world.Lighting.environment;
			if (env == null)
			{
				return;
			}
			if (rtEnabled)
			{
				if (!_postCaptured)
				{
					_origTonemap = env.TonemapMode;
					_origExposure = env.TonemapExposure;
					_origGlow = env.GlowEnabled;
					_origGlowBloom = env.GlowBloom;
					_origGlowIntensity = env.GlowIntensity;
					_origGlowStrength = env.GlowStrength;
					_origGlowHdrThreshold = env.GlowHdrThreshold;
						_origAmbientSource = env.AmbientLightSource;
					_origAmbientEnergy = env.AmbientLightEnergy;
					_origAdjustEnabled = env.AdjustmentEnabled;
					_origAdjustBrightness = env.AdjustmentBrightness;
					_origAdjustContrast = env.AdjustmentContrast;
					_origAdjustSaturation = env.AdjustmentSaturation;
					if (_world.Lighting.Sun?.GDLight is DirectionalLight3D capSun)
					{
						_origSunAngular = capSun.LightAngularDistance;
						_origSunBlur = capSun.ShadowBlur;
					}
					_postCaptured = true;
				}
				env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
				env.TonemapExposure = 1.0f;
				env.GlowEnabled = true;
				env.GlowBloom = 0.0f;
				env.GlowIntensity = 0.35f;
				env.GlowStrength = 1.0f;
				env.GlowHdrThreshold = 1.4f;
				env.AdjustmentEnabled = true;
				env.AdjustmentBrightness = 1.0f;
				env.AdjustmentContrast = 1.12f;
				env.AdjustmentSaturation = 1.2f;
				env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
				env.AmbientLightEnergy = 0.15f;
				if (_world.Lighting.Sun?.GDLight is DirectionalLight3D sun)
				{
					sun.LightAngularDistance = 2.0f;
					sun.ShadowBlur = 1.0f;
				}
				_postApplied = true;
			}
			else if (_postCaptured)
			{
				env.TonemapMode = _origTonemap;
				env.TonemapExposure = _origExposure;
				env.GlowEnabled = _origGlow;
				env.GlowBloom = _origGlowBloom;
				env.GlowIntensity = _origGlowIntensity;
				env.GlowStrength = _origGlowStrength;
				env.GlowHdrThreshold = _origGlowHdrThreshold;
					env.AmbientLightSource = _origAmbientSource;
				env.AmbientLightEnergy = _origAmbientEnergy;
				env.AdjustmentEnabled = _origAdjustEnabled;
				env.AdjustmentBrightness = _origAdjustBrightness;
				env.AdjustmentContrast = _origAdjustContrast;
				env.AdjustmentSaturation = _origAdjustSaturation;
				if (_world.Lighting.Sun?.GDLight is DirectionalLight3D sun)
				{
					sun.LightAngularDistance = _origSunAngular;
					sun.ShadowBlur = _origSunBlur;
				}
				_postApplied = false;
			}
		}
		catch (System.Exception)
		{
		}
	}

	private bool IsFirstPerson()
	{
		try
		{
			return _world?.Environment?.CurrentCamera?.IsFirstPerson ?? false;
		}
		catch (System.Exception)
		{
			return false;
		}
	}

	private void CollectRecursive(Node node, Skeleton3D skeleton, bool firstAvatar, List<MeshEntry> result)
	{
		if (result.Count >= MaxParts)
		{
			return;
		}
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			result.Add(new MeshEntry
			{
				Mesh = mesh,
				Skeleton = mesh.Skin != null ? skeleton : null,
				FirstAvatar = firstAvatar
			});
		}
		foreach (Node child in node.GetChildren())
		{
			CollectRecursive(child, skeleton, firstAvatar, result);
		}
	}

	private bool MeshSetChanged(List<MeshEntry> meshes)
	{
		if (meshes.Count != _trackedMeshes.Length)
		{
			return true;
		}
		for (int i = 0; i < meshes.Count; i++)
		{
			if (meshes[i].Mesh.Mesh.GetInstanceId() != _trackedMeshes[i])
			{
				return true;
			}
		}
		return false;
	}

	private void Regather(List<MeshEntry> meshes)
	{
		_parts.Clear();
		_totalVerts = 0;
		_topologyReady = false;
		for (int i = 0; i < TexSlots; i++)
		{
			_texSources[i] = null;
		}

		int accessorySlot = 2;
		List<RTShape> chunks = new();
		foreach (MeshEntry entry in meshes)
		{
			SkinPart? part = BuildSkinPart(entry);
			if (part == null)
			{
				continue;
			}

			int texSlot = NoTexture;
			if (entry.FirstAvatar)
			{
				if (entry.Skeleton != null)
				{
					texSlot = entry.Mesh.Name.ToString() == "Head" ? 1 : 0;
					_texSources[texSlot] ??= entry.Mesh;
				}
				else if (accessorySlot < TexSlots)
				{
					texSlot = accessorySlot;
					_texSources[texSlot] = entry.Mesh;
					accessorySlot++;
				}
			}
			part.TexSlot = texSlot;

			_parts.Add(part);
			_totalVerts += part.VertCount;
			chunks.Add(new RTShape
			{
				Positions = new float[part.VertCount * 3],
				Normals = new float[part.VertCount * 3],
				Uvs = part.ModelUv,
				Indices = part.Indices,
				Color = part.Color,
				TexSlot = texSlot
			});
		}

		_trackedMeshes = new ulong[meshes.Count];
		for (int i = 0; i < meshes.Count; i++)
		{
			_trackedMeshes[i] = meshes[i].Mesh.Mesh.GetInstanceId();
		}

		if (_parts.Count == 0)
		{
			_effect.SetAvatarChunks(Array.Empty<RTShape>());
			return;
		}

		float[] initial = new float[_totalVerts * 3];
		float[] initialNorm = new float[_totalVerts * 3];
		SkinFrame(initial, initialNorm);
		int off = 0;
		for (int c = 0; c < chunks.Count; c++)
		{
			int n = _parts[c].VertCount * 3;
			Array.Copy(initial, off, chunks[c].Positions, 0, n);
			Array.Copy(initialNorm, off, chunks[c].Normals, 0, n);
			off += n;
		}

		_topologyReady = true;
		_effect.SetAvatarChunks(chunks.ToArray());
	}

	private SkinPart? BuildSkinPart(MeshEntry entry)
	{
		MeshInstance3D meshInstance = entry.Mesh;
		Godot.Collections.Array arrays = meshInstance.Mesh.SurfaceGetArrays(0);
		Vector3[] verts = arrays[(int)Godot.Mesh.ArrayType.Vertex].As<Vector3[]>();
		if (verts == null || verts.Length == 0)
		{
			return null;
		}
		int[] indices = arrays[(int)Godot.Mesh.ArrayType.Index].As<int[]>();
		if (indices == null || indices.Length == 0)
		{
			indices = new int[verts.Length];
			for (int i = 0; i < verts.Length; i++)
			{
				indices[i] = i;
			}
		}
		Vector3[] norms = arrays[(int)Godot.Mesh.ArrayType.Normal].As<Vector3[]>();
		Vector2[] uvs = arrays[(int)Godot.Mesh.ArrayType.TexUV].As<Vector2[]>();
		float[] modelPos = new float[verts.Length * 3];
		float[] modelNorm = new float[verts.Length * 3];
		float[] modelUv = new float[verts.Length * 2];
		for (int i = 0; i < verts.Length; i++)
		{
			modelPos[i * 3 + 0] = verts[i].X;
			modelPos[i * 3 + 1] = verts[i].Y;
			modelPos[i * 3 + 2] = verts[i].Z;
			Vector3 n = (norms != null && i < norms.Length) ? norms[i] : Vector3.Zero;
			modelNorm[i * 3 + 0] = n.X;
			modelNorm[i * 3 + 1] = n.Y;
			modelNorm[i * 3 + 2] = n.Z;
			Vector2 uv = (uvs != null && i < uvs.Length) ? uvs[i] : Vector2.Zero;
			modelUv[i * 2 + 0] = uv.X;
			modelUv[i * 2 + 1] = uv.Y;
		}

		bool rigid = entry.Skeleton == null || meshInstance.Skin == null;
		int[] bones = Array.Empty<int>();
		float[] weights = Array.Empty<float>();
		int bonesPerVert = 0;
		Transform3D[] bindPose = Array.Empty<Transform3D>();
		int[] bindToBone = Array.Empty<int>();
		if (!rigid)
		{
			bones = arrays[(int)Godot.Mesh.ArrayType.Bones].As<int[]>() ?? Array.Empty<int>();
			weights = arrays[(int)Godot.Mesh.ArrayType.Weights].As<float[]>() ?? Array.Empty<float>();
			bonesPerVert = (bones.Length > 0) ? bones.Length / verts.Length : 0;
			Skin skin = meshInstance.Skin;
			int bindCount = skin.GetBindCount();
			bindPose = new Transform3D[bindCount];
			bindToBone = new int[bindCount];
			for (int b = 0; b < bindCount; b++)
			{
				bindPose[b] = skin.GetBindPose(b);
				int bone = skin.GetBindBone(b);
				if (bone < 0)
				{
					bone = entry.Skeleton!.FindBone(skin.GetBindName(b));
				}
				bindToBone[b] = bone;
			}
		}

		return new SkinPart
		{
			ModelPos = modelPos,
			ModelNorm = modelNorm,
			ModelUv = modelUv,
			Indices = indices,
			BoneIdx = bones,
			Weights = weights,
			BonesPerVert = bonesPerVert,
			VertCount = verts.Length,
			Color = GetPartColor(meshInstance),
			BindPose = bindPose,
			BindToBone = bindToBone,
			Skeleton = entry.Skeleton!,
			Mesh = meshInstance,
			Rigid = rigid
		};
	}

	private void SkinFrame(float[] positions, float[] normals)
	{
		int outV = 0;
		foreach (SkinPart part in _parts)
		{
			if (part.Rigid)
			{
				SkinRigid(part, positions, normals, outV);
				outV += part.VertCount;
				continue;
			}

			Skeleton3D skeleton = part.Skeleton;
			if (!Node.IsInstanceValid(skeleton) || !skeleton.IsInsideTree())
			{
				Park(part, positions, normals, outV);
				outV += part.VertCount;
				_trackedMeshes = Array.Empty<ulong>();
				continue;
			}

			Transform3D skeletonGlobal = skeleton.GlobalTransform;
			Basis globalBasis = skeletonGlobal.Basis;
			int boneCount = skeleton.GetBoneCount();
			Transform3D[] skinMatrix = new Transform3D[part.BindToBone.Length];
			for (int b = 0; b < skinMatrix.Length; b++)
			{
				int bone = part.BindToBone[b];
				Transform3D bonePose = (bone >= 0 && bone < boneCount) ? skeleton.GetBoneGlobalPose(bone) : Transform3D.Identity;
				skinMatrix[b] = bonePose * part.BindPose[b];
			}

			int bonesPerVert = part.BonesPerVert;
			for (int v = 0; v < part.VertCount; v++)
			{
				Vector3 modelPos = new(part.ModelPos[v * 3 + 0], part.ModelPos[v * 3 + 1], part.ModelPos[v * 3 + 2]);
				Vector3 modelNorm = new(part.ModelNorm[v * 3 + 0], part.ModelNorm[v * 3 + 1], part.ModelNorm[v * 3 + 2]);
				Vector3 skinned = Vector3.Zero;
				Vector3 skinnedNorm = Vector3.Zero;
				float weightSum = 0f;
				for (int k = 0; k < bonesPerVert; k++)
				{
					int index = v * bonesPerVert + k;
					if (index >= part.Weights.Length || index >= part.BoneIdx.Length)
					{
						break;
					}
					float w = part.Weights[index];
					if (w <= 0f)
					{
						continue;
					}
					int b = part.BoneIdx[index];
					if (b < 0 || b >= skinMatrix.Length)
					{
						continue;
					}
					skinned += w * (skinMatrix[b] * modelPos);
					skinnedNorm += w * (skinMatrix[b].Basis * modelNorm);
					weightSum += w;
				}
				if (weightSum <= 0.0001f)
				{
					skinned = modelPos;
					skinnedNorm = modelNorm;
				}
				Store(positions, normals, outV + v, skeletonGlobal * skinned, globalBasis * skinnedNorm);
			}
			outV += part.VertCount;
		}
	}

	private static void SkinRigid(SkinPart part, float[] positions, float[] normals, int outV)
	{
		MeshInstance3D mesh = part.Mesh;
		if (mesh == null || !Node.IsInstanceValid(mesh) || !mesh.IsInsideTree())
		{
			Park(part, positions, normals, outV);
			return;
		}
		Transform3D global = mesh.GlobalTransform;
		Basis basis = global.Basis;
		for (int v = 0; v < part.VertCount; v++)
		{
			Vector3 modelPos = new(part.ModelPos[v * 3 + 0], part.ModelPos[v * 3 + 1], part.ModelPos[v * 3 + 2]);
			Vector3 modelNorm = new(part.ModelNorm[v * 3 + 0], part.ModelNorm[v * 3 + 1], part.ModelNorm[v * 3 + 2]);
			Store(positions, normals, outV + v, global * modelPos, basis * modelNorm);
		}
	}

	private static void Park(SkinPart part, float[] positions, float[] normals, int outV)
	{
		for (int v = 0; v < part.VertCount; v++)
		{
			positions[(outV + v) * 3 + 0] = ParkPosition.X;
			positions[(outV + v) * 3 + 1] = ParkPosition.Y;
			positions[(outV + v) * 3 + 2] = ParkPosition.Z;
			normals[(outV + v) * 3 + 0] = 0f;
			normals[(outV + v) * 3 + 1] = 1f;
			normals[(outV + v) * 3 + 2] = 0f;
		}
	}

	private static void Store(float[] positions, float[] normals, int vertex, Vector3 world, Vector3 worldNorm)
	{
		positions[vertex * 3 + 0] = world.X;
		positions[vertex * 3 + 1] = world.Y;
		positions[vertex * 3 + 2] = world.Z;
		float len = worldNorm.Length();
		worldNorm = len > 1e-6f ? worldNorm / len : new Vector3(0f, 1f, 0f);
		normals[vertex * 3 + 0] = worldNorm.X;
		normals[vertex * 3 + 1] = worldNorm.Y;
		normals[vertex * 3 + 2] = worldNorm.Z;
	}

	private float[] ReadColors()
	{
		float[] colors = new float[_parts.Count * 3];
		for (int c = 0; c < _parts.Count; c++)
		{
			Vector3 col = (_parts[c].Mesh != null && Node.IsInstanceValid(_parts[c].Mesh))
				? GetPartColor(_parts[c].Mesh)
				: _parts[c].Color;
			colors[c * 3 + 0] = col.X;
			colors[c * 3 + 1] = col.Y;
			colors[c * 3 + 2] = col.Z;
		}
		return colors;
	}

	private Rid[] FetchTextures()
	{
		Rid[] rds = new Rid[TexSlots];
		for (int i = 0; i < TexSlots; i++)
		{
			rds[i] = FetchRdTexture(_texSources[i]);
		}
		return rds;
	}

	private static Rid FetchRdTexture(MeshInstance3D? mesh)
	{
		if (mesh == null || !Node.IsInstanceValid(mesh))
		{
			return new Rid();
		}
		Texture2D? texture = GetAlbedoTexture(mesh);
		if (texture == null)
		{
			return new Rid();
		}
		Rid serverTexture = texture.GetRid();
		return serverTexture.IsValid ? RenderingServer.TextureGetRdTexture(serverTexture, true) : new Rid();
	}

	private static Texture2D? GetAlbedoTexture(MeshInstance3D mesh)
	{
		Material? material = mesh.MaterialOverride;
		if (material == null && mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 0)
		{
			material = mesh.GetActiveMaterial(0);
		}
		if (material is ShaderMaterial shaderMaterial)
		{
			Variant value = shaderMaterial.GetShaderParameter("albedo_texture");
			return value.VariantType == Variant.Type.Object ? value.As<Texture2D>() : null;
		}
		if (material is StandardMaterial3D standardMaterial)
		{
			return standardMaterial.AlbedoTexture;
		}
		return null;
	}

	private static Vector3 GetPartColor(MeshInstance3D mesh)
	{
		Variant instance = mesh.GetInstanceShaderParameter("albedo");
		if (instance.VariantType == Variant.Type.Color)
		{
			Godot.Color ic = instance.As<Godot.Color>().SrgbToLinear();
			return new Vector3(ic.R, ic.G, ic.B);
		}

		Material? material = mesh.MaterialOverride;
		if (material == null && mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() > 0)
		{
			material = mesh.GetActiveMaterial(0);
		}

		Vector3 tint = Vector3.One;
		bool haveTint = false;
		if (material is ShaderMaterial shaderMaterial)
		{
			Variant value = shaderMaterial.GetShaderParameter("albedo");
			if (value.VariantType == Variant.Type.Color)
			{
				Godot.Color c = value.As<Godot.Color>().SrgbToLinear();
				tint = new Vector3(c.R, c.G, c.B);
				haveTint = true;
			}
		}
		else if (material is StandardMaterial3D standardMaterial)
		{
			Godot.Color c = standardMaterial.AlbedoColor.SrgbToLinear();
			tint = new Vector3(c.R, c.G, c.B);
			haveTint = true;
		}

		Texture2D? texture = GetAlbedoTexture(mesh);
		if (texture != null)
		{
			Vector3 avg = AverageTextureColor(texture);
			return haveTint ? tint * avg : avg;
		}

		return haveTint ? tint : new Vector3(0.6f, 0.6f, 0.6f);
	}

	private static Vector3 AverageTextureColor(Texture2D texture)
	{
		try
		{
			Image? image = texture.GetImage();
			if (image == null)
			{
				return Vector3.One;
			}
			if (image.IsCompressed())
			{
				image.Decompress();
			}
			image.Resize(1, 1, Image.Interpolation.Lanczos);
			Godot.Color c = image.GetPixel(0, 0).SrgbToLinear();
			return new Vector3(c.R, c.G, c.B);
		}
		catch (System.Exception)
		{
			return Vector3.One;
		}
	}

	private struct MeshEntry
	{
		public MeshInstance3D Mesh;
		public Skeleton3D? Skeleton;
		public bool FirstAvatar;
	}

	private sealed class SkinPart
	{
		public float[] ModelPos = Array.Empty<float>();
		public float[] ModelNorm = Array.Empty<float>();
		public float[] ModelUv = Array.Empty<float>();
		public MeshInstance3D Mesh = null!;
		public int[] Indices = Array.Empty<int>();
		public int[] BoneIdx = Array.Empty<int>();
		public float[] Weights = Array.Empty<float>();
		public int BonesPerVert;
		public int VertCount;
		public int TexSlot = 255;
		public Vector3 Color;
		public Transform3D[] BindPose = Array.Empty<Transform3D>();
		public int[] BindToBone = Array.Empty<int>();
		public Skeleton3D Skeleton = null!;
		public bool Rigid;
	}
}
