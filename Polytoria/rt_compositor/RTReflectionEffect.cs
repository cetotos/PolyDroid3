using Godot;
using System;
using System.Collections.Generic;

namespace Polytoria.Client.Rendering;

public sealed class RTShape
{
	public float[] Positions = Array.Empty<float>();
	public float[] Normals = Array.Empty<float>();
	public float[] Uvs = Array.Empty<float>();
	public int[] Indices = Array.Empty<int>();
	public Vector3 Color = new(0.6f, 0.6f, 0.6f);
	public int TexSlot = 255;
}

public struct RTInstance
{
	public int ShapeIndex;
	public Transform3D Transform;
	public float Roughness;
	public float Metallic;
	public Vector3 Color;
	public Vector3 Emission;
	public bool Transparent;
	public int TexLayer;
	public float StudsPerTile;
}

[GlobalClass]
public partial class RTReflectionEffect : CompositorEffect
{
	private RenderingDevice? _rd;
	private Rid _shader;
	private Rid _shadowShader;
	private Rid _pipeline;
	private Rid _sbt;
	private long _sbtRange;
	private Rid _tlas;
	private Rid _cameraBuffer;
	private Rid _positionsBuffer;
	private Rid _normalsBuffer;
	private Rid _indicesBuffer;
	private Rid _offsetsBuffer;
	private Rid _materialBuffer;
	private Rid _colorBuffer;
	private Rid _emissionBuffer;
	private Rid _texParamsBuffer;
	private Rid _worldTexArray;
	private Rid _worldSampler;
	private Image[]? _pendingWorldTextures;
	private Rid _lightsBuffer;
	private Rid _prevTransformBuffer;
	private float[]? _prevTransformData;
	private float[] _pendingLights = { 0f };
	private readonly object _lightsLock = new();
	private Rid _denoiseShader;
	private Rid _denoisePipeline;
	private Rid _temporalShader;
	private Rid _temporalPipeline;
	private Rid _atrousShader;
	private Rid _atrousPipeline;
	private Rid _reflAtrousShader;
	private Rid _reflAtrousPipeline;
	private Rid _reflTex;
	private Rid _dataTex;
	private Rid _giTex;
	private Rid _giHistoryTex;
	private Rid _giDenoisedTex;
	private Rid _giPosPrevTex;
	private Rid _giPosTex;
	private Rid _giPrevPosTex;
	private readonly Rid[] _giHistoryTexEye = new Rid[2];
	private readonly Rid[] _giDenoisedTexEye = new Rid[2];
	private readonly Rid[] _giPosTexEye = new Rid[2];
	private readonly Rid[] _giPosPrevTexEye = new Rid[2];
	private readonly Projection[] _prevViewProjEye = { Projection.Identity, Projection.Identity };
	private Rid _giAlbedoTex;
	private Rid _giNormalTex;
	private Rid _giPosFullTex;
	private Rid _giNormalFullTex;
	private Rid _giAtrousA;
	private Rid _giAtrousB;
	private Rid _volScatterTex;
	private Rid _reflScratch;
	private Projection _prevViewProj = Projection.Identity;
	private Projection _curViewProj = Projection.Identity;
	private Vector3 _lastCamOrigin;
	private long _frameCounter;

	public Vector3 LastCamOrigin => _lastCamOrigin;
	private Rid _sampler;
	private Vector2I _texSize;
	private readonly List<Rid> _ownedRids = new();

	private const int MaxProxies = 64;
	private const uint ProxyBit = 0x800000u;
	private Rid _proxyBlas;
	private Godot.Collections.Array<RDAccelerationStructureInstance>? _allInstances;
	private int _proxyBaseId = -1;
	private readonly object _proxyLock = new();
	private bool _tlasDirty;
	private int[]? _pendingDynIndices;
	private Transform3D[]? _pendingDynTransforms;
	private readonly object _dynLock = new();
	private readonly List<Rid> _chunkBlas = new();
	private readonly List<Rid> _chunkVertexBuffers = new();
	private readonly List<Rid> _chunkIndexBuffers = new();
	private readonly List<int> _chunkVertCounts = new();
	private readonly List<int> _chunkIndexCounts = new();
	private readonly List<int> _chunkVbase = new();
	private int _chunkPositionsLen;
	private int _chunkColorsLen;
	private RTShape[]? _pendingChunks;
	private bool _chunksBuilt;
	private float[]? _pendingPositions;
	private float[]? _pendingPrevPositions;
	private float[]? _pendingNormals;
	private float[]? _pendingColors;
	private bool _pendingParkProxies;
	private Rid _chunkPositionsBuffer;
	private Rid _chunkPositionsPrevBuffer;
	private Rid _chunkNormalsBuffer;
	private Rid _chunkIndicesBuffer;
	private Rid _chunkOffsetsBuffer;
	private Rid _chunkColorsBuffer;
	private Rid _chunkUvsBuffer;
	private Rid _whiteTex;
	private Rid _texSampler;
	private const int TexSlots = 6;
	private readonly Rid[] _texRds = new Rid[TexSlots];
	private readonly object _texLock = new();

	private bool _pipelineReady;
	private bool _sceneBuilt;

	private RTShape[]? _pendingShapes;
	private RTInstance[]? _pendingInstances;
	private readonly object _sceneLock = new();

	private Vector3 _sunDir = new Vector3(0.4f, 1.0f, 0.3f).Normalized();
	private Vector3 _sunColor = new(1.0f, 0.96f, 0.9f);
	private float _sunIntensity = 1.5f;
	private float _giStrength = 0.7f;
	private float _volDensity = 0.018f;
	private int _giSamples = 4;
	private int _reflSamples = 8;
	private Vector3 _skyTop = new(0.25f, 0.52f, 0.84f);
	private Vector3 _skyBottom = new(0.60f, 0.79f, 0.92f);
	private Vector3 _skyHorizon = new(0.79f, 0.87f, 0.91f);
	private Vector3 _proxyTint = new(0.6f, 0.6f, 0.62f);

	public RTReflectionEffect()
	{
		EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
		Enabled = true;
		RenderingServer.CallOnRenderThread(Callable.From(InitializePipeline));
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
		{
			FreeAllResources();
		}
		base._Notification(what);
	}

	private void FreeAllResources()
	{
		if (_rd == null)
		{
			return;
		}

		HashSet<ulong> freed = new();
		void Free(Rid rid)
		{
			if (rid.IsValid && freed.Add(rid.Id))
			{
				_rd.FreeRid(rid);
			}
		}

		foreach (Rid rid in _ownedRids) Free(rid);
		foreach (Rid rid in _chunkBlas) Free(rid);
		foreach (Rid rid in _chunkVertexBuffers) Free(rid);
		foreach (Rid rid in _chunkIndexBuffers) Free(rid);
		foreach (Rid rid in _giHistoryTexEye) Free(rid);
		foreach (Rid rid in _giDenoisedTexEye) Free(rid);
		foreach (Rid rid in _giPosTexEye) Free(rid);
		foreach (Rid rid in _giPosPrevTexEye) Free(rid);

		Free(_shader); Free(_shadowShader); Free(_pipeline); Free(_sbt);
		Free(_tlas); Free(_cameraBuffer); Free(_positionsBuffer); Free(_normalsBuffer);
		Free(_indicesBuffer); Free(_offsetsBuffer); Free(_materialBuffer); Free(_colorBuffer);
		Free(_emissionBuffer); Free(_texParamsBuffer); Free(_worldTexArray); Free(_worldSampler);
		Free(_lightsBuffer); Free(_prevTransformBuffer);
		Free(_denoiseShader); Free(_denoisePipeline); Free(_temporalShader); Free(_temporalPipeline);
		Free(_atrousShader); Free(_atrousPipeline); Free(_reflAtrousShader); Free(_reflAtrousPipeline);
		Free(_reflTex); Free(_dataTex); Free(_giTex); Free(_giHistoryTex); Free(_giDenoisedTex);
		Free(_giPosPrevTex); Free(_giPosTex); Free(_giPrevPosTex); Free(_giAlbedoTex); Free(_giNormalTex);
		Free(_giPosFullTex); Free(_giNormalFullTex); Free(_giAtrousA); Free(_giAtrousB);
		Free(_volScatterTex); Free(_reflScratch); Free(_sampler);
		Free(_proxyBlas);
		Free(_chunkPositionsBuffer); Free(_chunkPositionsPrevBuffer); Free(_chunkNormalsBuffer);
		Free(_chunkIndicesBuffer); Free(_chunkOffsetsBuffer); Free(_chunkColorsBuffer); Free(_chunkUvsBuffer);
		Free(_whiteTex); Free(_texSampler);

		_ownedRids.Clear();
		_chunkBlas.Clear();
		_chunkVertexBuffers.Clear();
		_chunkIndexBuffers.Clear();
	}

	public void SetScene(RTShape[] shapes, RTInstance[] instances)
	{
		lock (_sceneLock)
		{
			_pendingShapes = shapes;
			_pendingInstances = instances;
		}
	}

	public void SetWorldTextures(Image[] textures)
	{
		lock (_sceneLock)
		{
			_pendingWorldTextures = textures;
		}
	}

	public void SetEnvironment(Vector3 sunDir, Vector3 skyTop, Vector3 skyBottom, Vector3 skyHorizon)
	{
		_sunDir = sunDir.LengthSquared() > 0.0001f ? sunDir.Normalized() : _sunDir;
		_skyTop = skyTop;
		_skyBottom = skyBottom;
		_skyHorizon = skyHorizon;
	}

	public void SetSun(Vector3 color, float intensity)
	{
		_sunColor = color;
		_sunIntensity = Mathf.Max(0f, intensity);
	}

	public void SetLights(float[] packed)
	{
		lock (_lightsLock)
		{
			_pendingLights = (packed != null && packed.Length >= 1) ? packed : new float[] { 0f };
		}
	}

	public void SetRtParams(float giStrength, float volDensity, int giSamples, int reflSamples)
	{
		_giStrength = giStrength;
		_volDensity = volDensity;
		_giSamples = giSamples;
		_reflSamples = reflSamples;
	}

	public void SetProxyTint(Vector3 tint)
	{
		_proxyTint = tint;
	}

	public void SetAvatarChunks(RTShape[] chunks)
	{
		lock (_proxyLock)
		{
			_pendingChunks = chunks;
			_chunksBuilt = false;
		}
	}

	public void UpdateAvatar(float[] positions, float[] normals, float[] colors, float[] prevPositions)
	{
		lock (_proxyLock)
		{
			_pendingPositions = positions;
			_pendingPrevPositions = prevPositions;
			_pendingNormals = normals;
			_pendingColors = colors;
		}
	}

	public void UpdateDynamicParts(int[] indices, Transform3D[] transforms)
	{
		lock (_dynLock)
		{
			_pendingDynIndices = indices;
			_pendingDynTransforms = transforms;
		}
	}

	public void ParkProxies()
	{
		lock (_proxyLock)
		{
			_pendingParkProxies = true;
		}
	}

	public void SetAvatarTextures(Rid[] textures)
	{
		lock (_texLock)
		{
			for (int i = 0; i < TexSlots; i++)
			{
				_texRds[i] = (textures != null && i < textures.Length) ? textures[i] : new Rid();
			}
		}
	}

	private static Transform3D HiddenProxyTransform()
	{
		return new Transform3D(Basis.FromScale(Vector3.One * 0.0001f), new Vector3(0f, -100000f, 0f));
	}

	private static float[] ProxyCubePositions()
	{
		return new float[]
		{
			-0.5f, -0.5f, -0.5f, 0.5f, -0.5f, -0.5f, 0.5f, 0.5f, -0.5f, -0.5f, 0.5f, -0.5f,
			-0.5f, -0.5f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f, 0.5f, 0.5f, -0.5f, 0.5f, 0.5f
		};
	}

	private static int[] ProxyCubeIndices()
	{
		return new int[]
		{
			0, 1, 2, 0, 2, 3,
			4, 6, 5, 4, 7, 6,
			0, 3, 7, 0, 7, 4,
			1, 5, 6, 1, 6, 2,
			0, 4, 5, 0, 5, 1,
			3, 2, 6, 3, 6, 7
		};
	}

	private void UpdateProxies()
	{
		if (_allInstances == null || _proxyBaseId < 0 || _rd == null)
		{
			return;
		}

		RTShape[]? pendingChunks;
		float[]? pendingPositions;
		float[]? pendingPrevPositions;
		float[]? pendingNormals;
		float[]? pendingColors;
		bool parkProxies;
		lock (_proxyLock)
		{
			pendingChunks = _chunksBuilt ? null : _pendingChunks;
			pendingPositions = _pendingPositions;
			pendingPrevPositions = _pendingPrevPositions;
			pendingNormals = _pendingNormals;
			pendingColors = _pendingColors;
			parkProxies = _pendingParkProxies;
			_pendingPositions = null;
			_pendingPrevPositions = null;
			_pendingNormals = null;
			_pendingColors = null;
			_pendingParkProxies = false;
		}

		if (pendingChunks != null)
		{
			BuildChunks(pendingChunks);
		}

		if (pendingPositions != null && _chunksBuilt)
		{
			ApplyAvatar(pendingPositions, pendingNormals, pendingColors, pendingPrevPositions);
		}

		if (parkProxies)
		{
			for (int i = 0; i < MaxProxies; i++)
			{
				RDAccelerationStructureInstance inst = _allInstances[_proxyBaseId + i];
				inst.Transform = HiddenProxyTransform();
				inst.Blas = _proxyBlas;
				inst.Id = ProxyBit;
			}
			_tlasDirty = true;
		}

		ApplyDynamicPartTransforms();

		if (_tlasDirty && _tlas.IsValid)
		{
			_rd.TlasBuild(_tlas, _allInstances);
			_tlasDirty = false;
		}
	}

	private void ApplyDynamicPartTransforms()
	{
		int[]? indices;
		Transform3D[]? transforms;
		lock (_dynLock)
		{
			indices = _pendingDynIndices;
			transforms = _pendingDynTransforms;
			_pendingDynIndices = null;
			_pendingDynTransforms = null;
		}
		if (indices == null || transforms == null || _allInstances == null)
		{
			return;
		}
		for (int k = 0; k < indices.Length && k < transforms.Length; k++)
		{
			int i = indices[k];
			if (i >= 0 && i < _proxyBaseId)
			{
				RDAccelerationStructureInstance inst = _allInstances[i];
				inst.Transform = transforms[k];
			}
		}
		_tlasDirty = true;
	}

	private void WritePrevTransform(int i, Transform3D t)
	{
		if (_prevTransformData == null)
		{
			return;
		}
		int o = i * 12;
		Vector3 c0 = t.Basis.Column0;
		Vector3 c1 = t.Basis.Column1;
		Vector3 c2 = t.Basis.Column2;
		Vector3 tr = t.Origin;
		_prevTransformData[o + 0] = c0.X; _prevTransformData[o + 1] = c0.Y; _prevTransformData[o + 2] = c0.Z;
		_prevTransformData[o + 3] = c1.X; _prevTransformData[o + 4] = c1.Y; _prevTransformData[o + 5] = c1.Z;
		_prevTransformData[o + 6] = c2.X; _prevTransformData[o + 7] = c2.Y; _prevTransformData[o + 8] = c2.Z;
		_prevTransformData[o + 9] = tr.X; _prevTransformData[o + 10] = tr.Y; _prevTransformData[o + 11] = tr.Z;
	}

	private void UploadPrevTransforms()
	{
		if (_rd == null || _allInstances == null || _prevTransformData == null || !_prevTransformBuffer.IsValid)
		{
			return;
		}
		for (int i = 0; i < _proxyBaseId && i < _allInstances.Count; i++)
		{
			WritePrevTransform(i, _allInstances[i].Transform);
		}
		byte[] bytes = new byte[_prevTransformData.Length * 4];
		Buffer.BlockCopy(_prevTransformData, 0, bytes, 0, bytes.Length);
		_rd.BufferUpdate(_prevTransformBuffer, 0, (uint)bytes.Length, bytes);
	}

	private void BuildChunks(RTShape[] chunks)
	{
		if (_rd == null)
		{
			return;
		}
		RenderingDevice.BufferCreationBits asInput = RenderingDevice.BufferCreationBits.DeviceAddressBit | RenderingDevice.BufferCreationBits.AccelerationStructureBuildInputReadOnlyBit;

		foreach (Rid rid in _chunkBlas) { if (rid.IsValid && rid != _proxyBlas) { _rd.FreeRid(rid); } }
		foreach (Rid rid in _chunkVertexBuffers) { if (rid.IsValid) { _rd.FreeRid(rid); } }
		foreach (Rid rid in _chunkIndexBuffers) { if (rid.IsValid) { _rd.FreeRid(rid); } }
		_chunkBlas.Clear();
		_chunkVertexBuffers.Clear();
		_chunkIndexBuffers.Clear();
		_chunkVertCounts.Clear();
		_chunkIndexCounts.Clear();
		_chunkVbase.Clear();

		List<float> cPos = new();
		List<float> cNorm = new();
		List<float> cUv = new();
		List<int> cIdx = new();
		List<int> cOff = new();
		List<float> cCol = new();
		int vcount = 0;
		int icount = 0;

		foreach (RTShape chunk in chunks)
		{
			cOff.Add(vcount);
			cOff.Add(icount);
			cOff.Add(chunk.TexSlot);
			cCol.Add(chunk.Color.X);
			cCol.Add(chunk.Color.Y);
			cCol.Add(chunk.Color.Z);

			int chunkVerts = chunk.Positions.Length / 3;
			_chunkVbase.Add(vcount);
			_chunkVertCounts.Add(chunkVerts);
			_chunkIndexCounts.Add(chunk.Indices.Length);

			if (chunk.Positions.Length == 0 || chunk.Indices.Length == 0)
			{
				_chunkBlas.Add(_proxyBlas);
				_chunkVertexBuffers.Add(new Rid());
				_chunkIndexBuffers.Add(new Rid());
				continue;
			}

			byte[] vb = new byte[chunk.Positions.Length * 4];
			Buffer.BlockCopy(chunk.Positions, 0, vb, 0, vb.Length);
			Rid vbuf = _rd.VertexBufferCreate((uint)vb.Length, vb, asInput);
			byte[] ib = new byte[chunk.Indices.Length * 4];
			Buffer.BlockCopy(chunk.Indices, 0, ib, 0, ib.Length);
			Rid ibuf = _rd.IndexBufferCreate((uint)chunk.Indices.Length, RenderingDevice.IndexBufferFormat.Uint32, ib, false, asInput);
			_chunkVertexBuffers.Add(vbuf);
			_chunkIndexBuffers.Add(ibuf);
			_chunkBlas.Add(BuildChunkBlas(vbuf, ibuf, chunkVerts, chunk.Indices.Length));

			cPos.AddRange(chunk.Positions);
			if (chunk.Normals.Length == chunk.Positions.Length)
			{
				cNorm.AddRange(chunk.Normals);
			}
			else
			{
				for (int n = 0; n < chunk.Positions.Length; n++)
				{
					cNorm.Add(0f);
				}
			}
			if (chunk.Uvs.Length == chunkVerts * 2)
			{
				cUv.AddRange(chunk.Uvs);
			}
			else
			{
				for (int n = 0; n < chunkVerts * 2; n++)
				{
					cUv.Add(0f);
				}
			}
			cIdx.AddRange(chunk.Indices);
			vcount += chunkVerts;
			icount += chunk.Indices.Length;
		}

		if (cPos.Count == 0) { cPos.Add(0f); cPos.Add(0f); cPos.Add(0f); }
		if (cNorm.Count == 0) { cNorm.Add(0f); cNorm.Add(0f); cNorm.Add(0f); }
		if (cUv.Count == 0) { cUv.Add(0f); cUv.Add(0f); }
		if (cIdx.Count == 0) { cIdx.Add(0); cIdx.Add(0); cIdx.Add(0); }
		if (cOff.Count == 0) { cOff.Add(0); cOff.Add(0); cOff.Add(0); }
		if (cCol.Count == 0) { cCol.Add(0.6f); cCol.Add(0.6f); cCol.Add(0.6f); }

		_chunkPositionsLen = cPos.Count;
		_chunkColorsLen = cCol.Count;
		float[] cPosArr = cPos.ToArray();
		ReplaceBuffer(ref _chunkPositionsBuffer, cPosArr);
		ReplaceBuffer(ref _chunkPositionsPrevBuffer, cPosArr);
		ReplaceBuffer(ref _chunkNormalsBuffer, cNorm.ToArray());
		ReplaceBuffer(ref _chunkUvsBuffer, cUv.ToArray());
		ReplaceBuffer(ref _chunkIndicesBuffer, cIdx.ToArray());
		ReplaceBuffer(ref _chunkOffsetsBuffer, cOff.ToArray());
		ReplaceBuffer(ref _chunkColorsBuffer, cCol.ToArray());

		lock (_proxyLock)
		{
			_chunksBuilt = true;
		}
	}

	private Rid BuildChunkBlas(Rid vbuf, Rid ibuf, int vertCount, int indexCount)
	{
		RDAccelerationStructureGeometry geom = new()
		{
			VertexBuffer = vbuf,
			VertexCount = (uint)vertCount,
			VertexFormat = RenderingDevice.DataFormat.R32G32B32Sfloat,
			VertexStride = 12,
			IndexBuffer = ibuf,
			IndexCount = (uint)indexCount,
			Flags = RenderingDevice.AccelerationStructureGeometryFlagBits.OpaqueBit
		};
		Rid blas = _rd!.BlasCreate(new Godot.Collections.Array<RDAccelerationStructureGeometry> { geom }, RenderingDevice.AccelerationStructureFlagBits.PreferFastTraceBit);
		_rd.BlasBuild(blas);
		return blas;
	}

	private void ApplyAvatar(float[] positions, float[]? normals, float[]? colors, float[]? prevPositions)
	{
		if (_rd == null || _allInstances == null || _proxyBaseId < 0 || positions.Length != _chunkPositionsLen)
		{
			return;
		}

		byte[] posBytes = new byte[positions.Length * 4];
		Buffer.BlockCopy(positions, 0, posBytes, 0, posBytes.Length);
		_rd.BufferUpdate(_chunkPositionsBuffer, 0, (uint)posBytes.Length, posBytes);

		float[] prevSource = (prevPositions != null && prevPositions.Length == _chunkPositionsLen) ? prevPositions : positions;
		byte[] prevBytes = new byte[prevSource.Length * 4];
		Buffer.BlockCopy(prevSource, 0, prevBytes, 0, prevBytes.Length);
		_rd.BufferUpdate(_chunkPositionsPrevBuffer, 0, (uint)prevBytes.Length, prevBytes);

		if (normals != null && normals.Length == _chunkPositionsLen)
		{
			byte[] normBytes = new byte[normals.Length * 4];
			Buffer.BlockCopy(normals, 0, normBytes, 0, normBytes.Length);
			_rd.BufferUpdate(_chunkNormalsBuffer, 0, (uint)normBytes.Length, normBytes);
		}

		if (colors != null && colors.Length == _chunkColorsLen)
		{
			byte[] colBytes = new byte[colors.Length * 4];
			Buffer.BlockCopy(colors, 0, colBytes, 0, colBytes.Length);
			_rd.BufferUpdate(_chunkColorsBuffer, 0, (uint)colBytes.Length, colBytes);
		}

		for (int c = 0; c < _chunkVertexBuffers.Count; c++)
		{
			Rid vbuf = _chunkVertexBuffers[c];
			int vertCount = _chunkVertCounts[c];
			if (!vbuf.IsValid || vertCount == 0)
			{
				continue;
			}
			byte[] vbytes = new byte[vertCount * 3 * 4];
			Buffer.BlockCopy(positions, _chunkVbase[c] * 3 * 4, vbytes, 0, vbytes.Length);
			_rd.BufferUpdate(vbuf, 0, (uint)vbytes.Length, vbytes);

			if (_chunkBlas[c].IsValid && _chunkBlas[c] != _proxyBlas)
			{
				_rd.FreeRid(_chunkBlas[c]);
			}
			_chunkBlas[c] = BuildChunkBlas(vbuf, _chunkIndexBuffers[c], vertCount, _chunkIndexCounts[c]);
		}

		int count = _chunkBlas.Count;
		for (int i = 0; i < MaxProxies; i++)
		{
			RDAccelerationStructureInstance inst = _allInstances[_proxyBaseId + i];
			if (i < count && _chunkVertCounts[i] > 0 && _chunkBlas[i].IsValid)
			{
				inst.Transform = Transform3D.Identity;
				inst.Blas = _chunkBlas[i];
				inst.Id = ProxyBit | (uint)i;
			}
			else
			{
				inst.Transform = HiddenProxyTransform();
				inst.Blas = _proxyBlas;
				inst.Id = ProxyBit;
			}
		}
		_tlasDirty = true;
	}

	private void ReplaceBuffer(ref Rid buffer, float[] data)
	{
		if (buffer.IsValid)
		{
			_rd!.FreeRid(buffer);
		}
		buffer = CreateStorageBuffer(data);
	}

	private void ReplaceBuffer(ref Rid buffer, int[] data)
	{
		if (buffer.IsValid)
		{
			_rd!.FreeRid(buffer);
		}
		buffer = CreateStorageBuffer(data);
	}

	private void InitializePipeline()
	{
		_rd = RenderingServer.GetRenderingDevice();
		if (_rd == null)
		{
			GD.PushError("RTReflectionEffect: no RenderingDevice (Not Vulkan?).");
			return;
		}

		if (!_rd.HasFeature(RenderingDevice.Features.RaytracingPipeline))
		{
			GD.PushError("RTReflectionEffect: main rendering device does not support RT pipelines.");
			return;
		}

		RDShaderFile? rayFile = GD.Load<RDShaderFile>("res://rt_compositor/ray_scene.glsl");
		RDShaderFile? shadowFile = GD.Load<RDShaderFile>("res://rt_compositor/shadow_miss.glsl");
		if (rayFile == null || shadowFile == null)
		{
			GD.PushError("RTReflectionEffect: failed to load RT shaders.");
			return;
		}

		_shader = _rd.ShaderCreateFromSpirV(rayFile.GetSpirV());
		_shadowShader = _rd.ShaderCreateFromSpirV(shadowFile.GetSpirV());
		if (!_shader.IsValid || !_shadowShader.IsValid)
		{
			GD.PushError("RTReflectionEffect: RT shader creation failed.");
			return;
		}

		RDPipelineShader rayStage = new() { Shader = _shader };
		RDPipelineShader shadowStage = new() { Shader = _shadowShader };
		RDHitGroup hitGroup = new() { ClosestHitShader = rayStage };

		_pipeline = _rd.RaytracingPipelineCreate(
			new Godot.Collections.Array<RDPipelineShader> { rayStage },
			new Godot.Collections.Array<RDPipelineShader> { rayStage, shadowStage },
			new Godot.Collections.Array<RDHitGroup> { hitGroup },
			2);
		if (!_pipeline.IsValid)
		{
			GD.PushError("RTReflectionEffect: RaytracingPipelineCreate failed.");
			return;
		}

		_sbt = _rd.HitSbtCreate(_pipeline, 1024);
		_sbtRange = _rd.HitSbtRangeAlloc(_sbt, 1);
		_rd.HitSbtRangeUpdate(_sbt, _sbtRange, 0, new int[] { 0 });

		_cameraBuffer = _rd.UniformBufferCreate(368, new byte[368]);
		_sampler = _rd.SamplerCreate(new RDSamplerState());

		_chunkPositionsBuffer = CreateStorageBuffer(new float[3]);
		_chunkPositionsPrevBuffer = CreateStorageBuffer(new float[3]);
		_chunkNormalsBuffer = CreateStorageBuffer(new float[3]);
		_chunkIndicesBuffer = CreateStorageBuffer(new int[3]);
		_chunkOffsetsBuffer = CreateStorageBuffer(new int[3]);
		_chunkColorsBuffer = CreateStorageBuffer(new float[3]);
		_chunkUvsBuffer = CreateStorageBuffer(new float[2]);

		RDTextureFormat whiteFormat = new()
		{
			TextureType = RenderingDevice.TextureType.Type2D,
			Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
			Width = 1,
			Height = 1,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit
		};
		_whiteTex = _rd.TextureCreate(whiteFormat, new RDTextureView(), new Godot.Collections.Array<byte[]> { new byte[] { 255, 255, 255, 0 } });

		RDSamplerState texSamplerState = new()
		{
			MagFilter = RenderingDevice.SamplerFilter.Linear,
			MinFilter = RenderingDevice.SamplerFilter.Linear,
			RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
			RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge
		};
		_texSampler = _rd.SamplerCreate(texSamplerState);

		RDSamplerState worldSamplerState = new()
		{
			MagFilter = RenderingDevice.SamplerFilter.Linear,
			MinFilter = RenderingDevice.SamplerFilter.Linear,
			RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
			RepeatV = RenderingDevice.SamplerRepeatMode.Repeat
		};
		_worldSampler = _rd.SamplerCreate(worldSamplerState);

		RDShaderFile? denoiseFile = GD.Load<RDShaderFile>("res://rt_compositor/denoise_composite.glsl");
		if (denoiseFile == null)
		{
			GD.PushError("RTReflectionEffect: failed to load denoise_composite.glsl.");
			return;
		}
		_denoiseShader = _rd.ShaderCreateFromSpirV(denoiseFile.GetSpirV());
		if (!_denoiseShader.IsValid)
		{
			GD.PushError("RTReflectionEffect: denoise shader creation failed.");
			return;
		}
		_denoisePipeline = _rd.ComputePipelineCreate(_denoiseShader, new Godot.Collections.Array<RDPipelineSpecializationConstant>());
		if (!_denoisePipeline.IsValid)
		{
			GD.PushError("RTReflectionEffect: denoise pipeline creation failed.");
			return;
		}

		RDShaderFile? temporalFile = GD.Load<RDShaderFile>("res://rt_compositor/gi_temporal.glsl");
		if (temporalFile == null)
		{
			GD.PushError("RTReflectionEffect: failed to load gi_temporal.glsl.");
			return;
		}
		_temporalShader = _rd.ShaderCreateFromSpirV(temporalFile.GetSpirV());
		if (!_temporalShader.IsValid)
		{
			GD.PushError("RTReflectionEffect: temporal shader creation failed.");
			return;
		}
		_temporalPipeline = _rd.ComputePipelineCreate(_temporalShader, new Godot.Collections.Array<RDPipelineSpecializationConstant>());
		if (!_temporalPipeline.IsValid)
		{
			GD.PushError("RTReflectionEffect: temporal pipeline creation failed.");
			return;
		}

		RDShaderFile? atrousFile = GD.Load<RDShaderFile>("res://rt_compositor/gi_atrous.glsl");
		if (atrousFile == null)
		{
			GD.PushError("RTReflectionEffect: failed to load gi_atrous.glsl.");
			return;
		}
		_atrousShader = _rd.ShaderCreateFromSpirV(atrousFile.GetSpirV());
		if (!_atrousShader.IsValid)
		{
			GD.PushError("RTReflectionEffect: atrous shader creation failed.");
			return;
		}
		_atrousPipeline = _rd.ComputePipelineCreate(_atrousShader, new Godot.Collections.Array<RDPipelineSpecializationConstant>());
		if (!_atrousPipeline.IsValid)
		{
			GD.PushError("RTReflectionEffect: atrous creation failed.");
			return;
		}

		RDShaderFile? reflAtrousFile = GD.Load<RDShaderFile>("res://rt_compositor/gi_refl_atrous.glsl");
		if (reflAtrousFile == null)
		{
			GD.PushError("RTReflectionEffect: failed to load gi_refl_atrous.glsl.");
			return;
		}
		_reflAtrousShader = _rd.ShaderCreateFromSpirV(reflAtrousFile.GetSpirV());
		if (!_reflAtrousShader.IsValid)
		{
			GD.PushError("RTReflectionEffect: refl atrous shader creation failed.");
			return;
		}
		_reflAtrousPipeline = _rd.ComputePipelineCreate(_reflAtrousShader, new Godot.Collections.Array<RDPipelineSpecializationConstant>());
		if (!_reflAtrousPipeline.IsValid)
		{
			GD.PushError("RTReflectionEffect: refl atrous pipeline creation failed.");
			return;
		}


		_pipelineReady = true;
	}

	private void EnsureTextures(Vector2I size)
	{
		if (_reflTex.IsValid && size == _texSize)
		{
			return;
		}
		if (_reflTex.IsValid)
		{
			_rd!.FreeRid(_reflTex);
		}
		if (_dataTex.IsValid)
		{
			_rd!.FreeRid(_dataTex);
		}
		if (_giTex.IsValid)
		{
			_rd!.FreeRid(_giTex);
		}
		for (int eye = 0; eye < 2; eye++)
		{
			if (_giHistoryTexEye[eye].IsValid) _rd!.FreeRid(_giHistoryTexEye[eye]);
			if (_giDenoisedTexEye[eye].IsValid) _rd!.FreeRid(_giDenoisedTexEye[eye]);
			if (_giPosTexEye[eye].IsValid) _rd!.FreeRid(_giPosTexEye[eye]);
			if (_giPosPrevTexEye[eye].IsValid) _rd!.FreeRid(_giPosPrevTexEye[eye]);
		}
		if (_giPrevPosTex.IsValid)
		{
			_rd!.FreeRid(_giPrevPosTex);
		}
		if (_giAlbedoTex.IsValid)
		{
			_rd!.FreeRid(_giAlbedoTex);
		}
		if (_giNormalTex.IsValid)
		{
			_rd!.FreeRid(_giNormalTex);
		}
		if (_giPosFullTex.IsValid)
		{
			_rd!.FreeRid(_giPosFullTex);
		}
		if (_giNormalFullTex.IsValid)
		{
			_rd!.FreeRid(_giNormalFullTex);
		}
		if (_giAtrousA.IsValid)
		{
			_rd!.FreeRid(_giAtrousA);
		}
		if (_giAtrousB.IsValid)
		{
			_rd!.FreeRid(_giAtrousB);
		}
		if (_volScatterTex.IsValid)
		{
			_rd!.FreeRid(_volScatterTex);
		}
		if (_reflScratch.IsValid)
		{
			_rd!.FreeRid(_reflScratch);
		}

		_texSize = size;
		RDTextureFormat format = new()
		{
			TextureType = RenderingDevice.TextureType.Type2D,
			Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
			Width = (uint)size.X,
			Height = (uint)size.Y,
			UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanCopyFromBit
		};
		RDTextureView view = new();
		_reflTex = _rd!.TextureCreate(format, view, new Godot.Collections.Array<byte[]>());
		_dataTex = _rd!.TextureCreate(format, view, new Godot.Collections.Array<byte[]>());
		_giAlbedoTex = _rd!.TextureCreate(format, view, new Godot.Collections.Array<byte[]>());
		_giNormalFullTex = _rd!.TextureCreate(format, view, new Godot.Collections.Array<byte[]>());
		_volScatterTex = _rd!.TextureCreate(format, view, new Godot.Collections.Array<byte[]>());
		_reflScratch = _rd!.TextureCreate(format, view, new Godot.Collections.Array<byte[]>());

		RDTextureFormat posFormat = new()
		{
			TextureType = RenderingDevice.TextureType.Type2D,
			Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
			Width = (uint)size.X,
			Height = (uint)size.Y,
			UsageBits = RenderingDevice.TextureUsageBits.StorageBit
		};
		_giPosFullTex = _rd!.TextureCreate(posFormat, view, new Godot.Collections.Array<byte[]>());

		Vector2I halfSize = HalfSize(size);
		RDTextureFormat halfFormat = new()
		{
			TextureType = RenderingDevice.TextureType.Type2D,
			Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
			Width = (uint)halfSize.X,
			Height = (uint)halfSize.Y,
			UsageBits = RenderingDevice.TextureUsageBits.StorageBit
		};
		RDTextureFormat halfPosFormat = new()
		{
			TextureType = RenderingDevice.TextureType.Type2D,
			Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
			Width = (uint)halfSize.X,
			Height = (uint)halfSize.Y,
			UsageBits = RenderingDevice.TextureUsageBits.StorageBit
		};
		_giTex = _rd!.TextureCreate(halfFormat, view, new Godot.Collections.Array<byte[]>());
		_giNormalTex = _rd!.TextureCreate(halfFormat, view, new Godot.Collections.Array<byte[]>());
		_giAtrousA = _rd!.TextureCreate(halfFormat, view, new Godot.Collections.Array<byte[]>());
		_giAtrousB = _rd!.TextureCreate(halfFormat, view, new Godot.Collections.Array<byte[]>());
		_giPrevPosTex = _rd!.TextureCreate(halfPosFormat, view, new Godot.Collections.Array<byte[]>());

		for (int eye = 0; eye < 2; eye++)
		{
			_giHistoryTexEye[eye] = _rd!.TextureCreate(halfFormat, view, new Godot.Collections.Array<byte[]>());
			_giDenoisedTexEye[eye] = _rd!.TextureCreate(halfFormat, view, new Godot.Collections.Array<byte[]>());
			_giPosTexEye[eye] = _rd!.TextureCreate(halfPosFormat, view, new Godot.Collections.Array<byte[]>());
			_giPosPrevTexEye[eye] = _rd!.TextureCreate(halfPosFormat, view, new Godot.Collections.Array<byte[]>());
			_prevViewProjEye[eye] = Projection.Identity;
		}
	}

	private static Vector2I HalfSize(Vector2I size) => new((size.X + 1) / 2, (size.Y + 1) / 2);

	private void BuildScene(RTShape[] shapes, RTInstance[] instances)
	{
		if (_rd == null)
		{
			return;
		}

		List<float> allPositions = new();
		List<float> allNormals = new();
		List<int> allIndices = new();
		int[] shapeVbase = new int[shapes.Length];
		int[] shapeIbase = new int[shapes.Length];
		Rid[] shapeBlas = new Rid[shapes.Length];
		bool[] shapeValid = new bool[shapes.Length];

		RenderingDevice.BufferCreationBits asInput = RenderingDevice.BufferCreationBits.DeviceAddressBit | RenderingDevice.BufferCreationBits.AccelerationStructureBuildInputReadOnlyBit;

		int vcount = 0;
		int icount = 0;

		for (int s = 0; s < shapes.Length; s++)
		{
			RTShape shape = shapes[s];
			if (shape.Positions.Length == 0 || shape.Indices.Length == 0)
			{
				continue;
			}

			byte[] vbytes = new byte[shape.Positions.Length * 4];
			Buffer.BlockCopy(shape.Positions, 0, vbytes, 0, vbytes.Length);
			Rid vbuf = _rd.VertexBufferCreate((uint)vbytes.Length, vbytes, asInput);
			_ownedRids.Add(vbuf);

			byte[] ibytes = new byte[shape.Indices.Length * 4];
			Buffer.BlockCopy(shape.Indices, 0, ibytes, 0, ibytes.Length);
			Rid ibuf = _rd.IndexBufferCreate((uint)shape.Indices.Length, RenderingDevice.IndexBufferFormat.Uint32, ibytes, false, asInput);
			_ownedRids.Add(ibuf);

			RDAccelerationStructureGeometry geom = new()
			{
				VertexBuffer = vbuf,
				VertexCount = (uint)(shape.Positions.Length / 3),
				VertexFormat = RenderingDevice.DataFormat.R32G32B32Sfloat,
				VertexStride = 12,
				IndexBuffer = ibuf,
				IndexCount = (uint)shape.Indices.Length,
				Flags = RenderingDevice.AccelerationStructureGeometryFlagBits.OpaqueBit
			};

			Rid blas = _rd.BlasCreate(new Godot.Collections.Array<RDAccelerationStructureGeometry> { geom }, RenderingDevice.AccelerationStructureFlagBits.PreferFastTraceBit);
			_rd.BlasBuild(blas);
			_ownedRids.Add(blas);

			shapeVbase[s] = vcount;
			shapeIbase[s] = icount;
			shapeBlas[s] = blas;
			shapeValid[s] = true;

			allPositions.AddRange(shape.Positions);
			if (shape.Normals.Length == shape.Positions.Length)
			{
				allNormals.AddRange(shape.Normals);
			}
			else
			{
				for (int n = 0; n < shape.Positions.Length; n++)
				{
					allNormals.Add(0f);
				}
			}
			allIndices.AddRange(shape.Indices);

			vcount += shape.Positions.Length / 3;
			icount += shape.Indices.Length;
		}

		List<int> offsets = new();
		List<float> materials = new();
		List<float> colors = new();
		List<float> emissions = new();
		List<float> texParams = new();
		Godot.Collections.Array<RDAccelerationStructureInstance> asInstances = new();

		foreach (RTInstance inst in instances)
		{
			if (inst.ShapeIndex < 0 || inst.ShapeIndex >= shapes.Length || !shapeValid[inst.ShapeIndex])
			{
				continue;
			}

			offsets.Add(shapeVbase[inst.ShapeIndex]);
			offsets.Add(shapeIbase[inst.ShapeIndex]);
			materials.Add(inst.Roughness);
			materials.Add(inst.Metallic);
			materials.Add(inst.Transparent ? 1f : 0f);
			texParams.Add(inst.TexLayer);
			texParams.Add(inst.StudsPerTile);
			colors.Add(inst.Color.X);
			colors.Add(inst.Color.Y);
			colors.Add(inst.Color.Z);
			emissions.Add(inst.Emission.X);
			emissions.Add(inst.Emission.Y);
			emissions.Add(inst.Emission.Z);

			asInstances.Add(new RDAccelerationStructureInstance
			{
				Blas = shapeBlas[inst.ShapeIndex],
				Transform = inst.Transform,
				HitSbtRange = _sbtRange,
				Mask = inst.Transparent ? (byte)0x04 : (byte)0x01,
				Id = (uint)asInstances.Count
			});
		}

		if (asInstances.Count == 0)
		{
			GD.PushError("RTReflectionEffect: cant build acceleration structures.");
			return;
		}

		_proxyBaseId = asInstances.Count;
		{
			float[] cubePos = ProxyCubePositions();
			int[] cubeIdx = ProxyCubeIndices();

			byte[] vb = new byte[cubePos.Length * 4];
			Buffer.BlockCopy(cubePos, 0, vb, 0, vb.Length);
			Rid vbuf = _rd.VertexBufferCreate((uint)vb.Length, vb, asInput);
			_ownedRids.Add(vbuf);
			byte[] ib = new byte[cubeIdx.Length * 4];
			Buffer.BlockCopy(cubeIdx, 0, ib, 0, ib.Length);
			Rid ibuf = _rd.IndexBufferCreate((uint)cubeIdx.Length, RenderingDevice.IndexBufferFormat.Uint32, ib, false, asInput);
			_ownedRids.Add(ibuf);

			RDAccelerationStructureGeometry geom = new()
			{
				VertexBuffer = vbuf,
				VertexCount = (uint)(cubePos.Length / 3),
				VertexFormat = RenderingDevice.DataFormat.R32G32B32Sfloat,
				VertexStride = 12,
				IndexBuffer = ibuf,
				IndexCount = (uint)cubeIdx.Length,
				Flags = RenderingDevice.AccelerationStructureGeometryFlagBits.OpaqueBit
			};
			_proxyBlas = _rd.BlasCreate(new Godot.Collections.Array<RDAccelerationStructureGeometry> { geom }, RenderingDevice.AccelerationStructureFlagBits.PreferFastTraceBit);
			_rd.BlasBuild(_proxyBlas);
			_ownedRids.Add(_proxyBlas);

			for (int i = 0; i < MaxProxies; i++)
			{
				asInstances.Add(new RDAccelerationStructureInstance
				{
					Blas = _proxyBlas,
					Transform = HiddenProxyTransform(),
					HitSbtRange = _sbtRange,
					Mask = (byte)0x02,
					Id = ProxyBit
				});
			}
		}

		_allInstances = asInstances;

		_positionsBuffer = CreateStorageBuffer(allPositions.ToArray());
		_normalsBuffer = CreateStorageBuffer(allNormals.ToArray());
		_indicesBuffer = CreateStorageBuffer(allIndices.ToArray());
		_offsetsBuffer = CreateStorageBuffer(offsets.ToArray());
		_materialBuffer = CreateStorageBuffer(materials.ToArray());
		_colorBuffer = CreateStorageBuffer(colors.ToArray());
		_emissionBuffer = CreateStorageBuffer(emissions.ToArray());
		_texParamsBuffer = CreateStorageBuffer(texParams.Count > 0 ? texParams.ToArray() : new float[] { -1f, 2f });
		Image[]? worldTextures;
		lock (_sceneLock)
		{
			worldTextures = _pendingWorldTextures;
			_pendingWorldTextures = null;
		}
		_worldTexArray = BuildWorldTextureArray(worldTextures);
		_prevTransformData = new float[Mathf.Max(_proxyBaseId, 1) * 12];
		for (int i = 0; i < _proxyBaseId; i++)
		{
			WritePrevTransform(i, _allInstances[i].Transform);
		}
		_prevTransformBuffer = CreateStorageBuffer(_prevTransformData);
		float[] lightData;
		lock (_lightsLock)
		{
			lightData = _pendingLights;
		}
		_lightsBuffer = CreateStorageBuffer(lightData);

		_tlas = _rd.TlasCreate((uint)asInstances.Count, RenderingDevice.AccelerationStructureFlagBits.PreferFastTraceBit);
		_rd.TlasBuild(_tlas, asInstances);

		_sceneBuilt = true;
	}

	private Rid BuildWorldTextureArray(Image[]? images)
	{
		bool hasImages = images != null && images.Length > 0;
		uint width = hasImages ? (uint)images![0].GetWidth() : 1u;
		uint height = hasImages ? (uint)images![0].GetHeight() : 1u;
		Godot.Collections.Array<byte[]> data = new();
		if (hasImages)
		{
			int expected = (int)(width * height * 4);
			foreach (Image img in images!)
			{
				byte[] bytes = img.GetData();
				if (img.GetWidth() != (int)width || img.GetHeight() != (int)height || bytes.Length != expected)
				{
					GD.PushError("world texture layer size mismatch! dropping textures");
					hasImages = false;
					break;
				}
				data.Add(bytes);
			}
		}
		if (!hasImages)
		{
			width = 1;
			height = 1;
			data.Clear();
			data.Add(new byte[] { 255, 255, 255, 255 });
		}
		RDTextureFormat format = new()
		{
			TextureType = RenderingDevice.TextureType.Type2DArray,
			Format = RenderingDevice.DataFormat.R8G8B8A8Srgb,
			Width = width,
			Height = height,
			ArrayLayers = hasImages ? (uint)images!.Length : 1u,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit
		};
		Rid tex = _rd!.TextureCreate(format, new RDTextureView(), data);
		if (!tex.IsValid)
		{
			GD.PushError("world texture creation failed! using white");
			format.Width = 1;
			format.Height = 1;
			format.ArrayLayers = 1;
			tex = _rd!.TextureCreate(format, new RDTextureView(), new Godot.Collections.Array<byte[]> { new byte[] { 255, 255, 255, 255 } });
		}
		_ownedRids.Add(tex);
		return tex;
	}

	private Rid CreateStorageBuffer(float[] data)
	{
		byte[] bytes = new byte[data.Length * 4];
		Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
		return _rd!.StorageBufferCreate((uint)bytes.Length, bytes, (RenderingDevice.StorageBufferUsage)0);
	}

	private Rid CreateStorageBuffer(int[] data)
	{
		byte[] bytes = new byte[data.Length * 4];
		Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
		return _rd!.StorageBufferCreate((uint)bytes.Length, bytes, (RenderingDevice.StorageBufferUsage)0);
	}

	public override void _RenderCallback(int effectCallbackType, RenderData renderData)
	{
		if (!_pipelineReady || _rd == null)
		{
			return;
		}
		if (effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent)
		{
			return;
		}

		if (!_sceneBuilt)
		{
			RTShape[]? shapes;
			RTInstance[]? instances;
			lock (_sceneLock)
			{
				shapes = _pendingShapes;
				instances = _pendingInstances;
			}
			if (shapes == null || instances == null)
			{
				return;
			}
			BuildScene(shapes, instances);
			if (!_sceneBuilt)
			{
				return;
			}
		}

		if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD sceneBuffers)
		{
			return;
		}
		if (renderData.GetRenderSceneData() is not RenderSceneDataRD sceneData)
		{
			return;
		}

		Vector2I size = sceneBuffers.GetInternalSize();
		if (size.X == 0 || size.Y == 0)
		{
			return;
		}

		EnsureTextures(size);
		UploadPrevTransforms();
		UpdateProxies();

		uint xGroups = ((uint)size.X - 1) / 8 + 1;
		uint yGroups = ((uint)size.Y - 1) / 8 + 1;
		Vector2I halfSize = HalfSize(size);
		uint hxGroups = ((uint)halfSize.X - 1) / 8 + 1;
		uint hyGroups = ((uint)halfSize.Y - 1) / 8 + 1;
		byte[] pushConstant = BuildPushConstant(size);

		uint viewCount = sceneBuffers.GetViewCount();
		for (uint view = 0; view < viewCount; view++)
		{
			Rid colorImage = sceneBuffers.GetColorLayer(view);
			if (!colorImage.IsValid)
			{
				continue;
			}

			int eye = view < 2u ? (int)view : 1;
			_giHistoryTex = _giHistoryTexEye[eye];
			_giDenoisedTex = _giDenoisedTexEye[eye];
			_giPosTex = _giPosTexEye[eye];
			_giPosPrevTex = _giPosPrevTexEye[eye];
			_prevViewProj = _prevViewProjEye[eye];
			UpdateCamera(sceneData, view, viewCount);

			Rid depthImage = sceneBuffers.GetDepthLayer(view);
			Rid traceSet = BuildTraceUniformSet(colorImage, depthImage);
			long rayList = _rd.RaytracingListBegin();
			_rd.RaytracingListBindRaytracingPipeline(rayList, _pipeline);
			_rd.RaytracingListBindUniformSet(rayList, traceSet, 0);
			_rd.RaytracingListTraceRays(rayList, 0, _sbt, (uint)size.X, (uint)size.Y, 1);
			_rd.RaytracingListEnd();

			Rid temporalSet = BuildTemporalUniformSet();
			byte[] temporalPush = BuildTemporalPushConstant(_prevViewProj, halfSize);
			long temporalList = _rd.ComputeListBegin();
			_rd.ComputeListBindComputePipeline(temporalList, _temporalPipeline);
			_rd.ComputeListBindUniformSet(temporalList, temporalSet, 0);
			_rd.ComputeListSetPushConstant(temporalList, temporalPush, (uint)temporalPush.Length);
			_rd.ComputeListDispatch(temporalList, hxGroups, hyGroups, 1);
			_rd.ComputeListEnd();

			const int atrousIterations = 3;
			Rid atrousSrc = _giDenoisedTex;
			for (int it = 0; it < atrousIterations; it++)
			{
				Rid atrousDst = (it % 2 == 0) ? _giAtrousA : _giAtrousB;
				Rid atrousSet = BuildAtrousUniformSet(atrousSrc, atrousDst);
				byte[] atrousPush = BuildAtrousPushConstant(halfSize, (uint)(1 << it));
				long atrousList = _rd.ComputeListBegin();
				_rd.ComputeListBindComputePipeline(atrousList, _atrousPipeline);
				_rd.ComputeListBindUniformSet(atrousList, atrousSet, 0);
				_rd.ComputeListSetPushConstant(atrousList, atrousPush, (uint)atrousPush.Length);
				_rd.ComputeListDispatch(atrousList, hxGroups, hyGroups, 1);
				_rd.ComputeListEnd();
				atrousSrc = atrousDst;
			}

			const int reflAtrousIterations = 4;
			Rid reflSrc = _reflTex;
			for (int it = 0; _reflSamples > 0 && it < reflAtrousIterations; it++)
			{
				Rid reflDst = (it % 2 == 0) ? _reflScratch : _reflTex;
				Rid reflSet = BuildReflAtrousUniformSet(reflSrc, reflDst);
				byte[] reflPush = BuildAtrousPushConstant(size, (uint)(1 << it));
				long reflList = _rd.ComputeListBegin();
				_rd.ComputeListBindComputePipeline(reflList, _reflAtrousPipeline);
				_rd.ComputeListBindUniformSet(reflList, reflSet, 0);
				_rd.ComputeListSetPushConstant(reflList, reflPush, (uint)reflPush.Length);
				_rd.ComputeListDispatch(reflList, xGroups, yGroups, 1);
				_rd.ComputeListEnd();
				reflSrc = reflDst;
			}

			Rid compositeSet = BuildCompositeUniformSet(colorImage, atrousSrc);
			long computeList = _rd.ComputeListBegin();
			_rd.ComputeListBindComputePipeline(computeList, _denoisePipeline);
			_rd.ComputeListBindUniformSet(computeList, compositeSet, 0);
			_rd.ComputeListSetPushConstant(computeList, pushConstant, (uint)pushConstant.Length);
			_rd.ComputeListDispatch(computeList, xGroups, yGroups, 1);
			_rd.ComputeListEnd();

			(_giHistoryTex, _giDenoisedTex) = (_giDenoisedTex, _giHistoryTex);
			(_giPosTex, _giPosPrevTex) = (_giPosPrevTex, _giPosTex);
			_giHistoryTexEye[eye] = _giHistoryTex;
			_giDenoisedTexEye[eye] = _giDenoisedTex;
			_giPosTexEye[eye] = _giPosTex;
			_giPosPrevTexEye[eye] = _giPosPrevTex;
			_prevViewProjEye[eye] = _curViewProj;
		}
	}

	private static byte[] BuildPushConstant(Vector2I size)
	{
		int[] values = { size.X, size.Y, 0, 0 };
		byte[] bytes = new byte[16];
		Buffer.BlockCopy(values, 0, bytes, 0, 16);
		return bytes;
	}

	private void UpdateCamera(RenderSceneDataRD sceneData, uint view, uint viewCount)
	{
		Transform3D camTransform = sceneData.GetCamTransform();
		if (viewCount > 1)
		{
			camTransform.Origin += camTransform.Basis * sceneData.GetViewEyeOffset(view);
		}
		Projection proj = sceneData.GetCamProjection();
		Projection viewProj = sceneData.GetViewProjection(view);
		Projection invProj = proj.Inverse();
		_curViewProj = proj * new Projection(camTransform.AffineInverse());

		Vector3 origin = camTransform.Origin;
		_lastCamOrigin = origin;
		Vector3 right = camTransform.Basis.Column0;
		Vector3 up = camTransform.Basis.Column1;
		Vector3 forward = -camTransform.Basis.Column2;

		float rightScale = Mathf.Abs(proj.X.X) > 0.00001f ? 1.0f / Mathf.Abs(proj.X.X) : 1.0f;
		float upScale = Mathf.Abs(proj.Y.Y) > 0.00001f ? 1.0f / Mathf.Abs(proj.Y.Y) : 1.0f;
		right *= rightScale;
		up *= upScale;

		float[] data = new float[92];
		data[0] = origin.X; data[1] = origin.Y; data[2] = origin.Z; data[3] = 0f;
		data[4] = right.X; data[5] = right.Y; data[6] = right.Z; data[7] = 0f;
		data[8] = up.X; data[9] = up.Y; data[10] = up.Z; data[11] = 0f;
		data[12] = forward.X; data[13] = forward.Y; data[14] = forward.Z; data[15] = 0f;
		data[16] = viewProj.X.X; data[17] = viewProj.X.Y; data[18] = viewProj.X.Z; data[19] = viewProj.X.W;
		data[20] = viewProj.Y.X; data[21] = viewProj.Y.Y; data[22] = viewProj.Y.Z; data[23] = viewProj.Y.W;
		data[24] = viewProj.Z.X; data[25] = viewProj.Z.Y; data[26] = viewProj.Z.Z; data[27] = viewProj.Z.W;
		data[28] = viewProj.W.X; data[29] = viewProj.W.Y; data[30] = viewProj.W.Z; data[31] = viewProj.W.W;
		data[32] = _sunDir.X; data[33] = _sunDir.Y; data[34] = _sunDir.Z; data[35] = 0f;
		data[36] = _skyTop.X; data[37] = _skyTop.Y; data[38] = _skyTop.Z; data[39] = 0f;
		data[40] = _skyBottom.X; data[41] = _skyBottom.Y; data[42] = _skyBottom.Z; data[43] = 0f;
		data[44] = _skyHorizon.X; data[45] = _skyHorizon.Y; data[46] = _skyHorizon.Z; data[47] = 0f;
		data[48] = invProj.X.X; data[49] = invProj.X.Y; data[50] = invProj.X.Z; data[51] = invProj.X.W;
		data[52] = invProj.Y.X; data[53] = invProj.Y.Y; data[54] = invProj.Y.Z; data[55] = invProj.Y.W;
		data[56] = invProj.Z.X; data[57] = invProj.Z.Y; data[58] = invProj.Z.Z; data[59] = invProj.Z.W;
		data[60] = invProj.W.X; data[61] = invProj.W.Y; data[62] = invProj.W.Z; data[63] = invProj.W.W;
		data[64] = _proxyTint.X; data[65] = _proxyTint.Y; data[66] = _proxyTint.Z; data[67] = (float)(_frameCounter % 65536);
		_frameCounter++;
		data[68] = _prevViewProj.X.X; data[69] = _prevViewProj.X.Y; data[70] = _prevViewProj.X.Z; data[71] = _prevViewProj.X.W;
		data[72] = _prevViewProj.Y.X; data[73] = _prevViewProj.Y.Y; data[74] = _prevViewProj.Y.Z; data[75] = _prevViewProj.Y.W;
		data[76] = _prevViewProj.Z.X; data[77] = _prevViewProj.Z.Y; data[78] = _prevViewProj.Z.Z; data[79] = _prevViewProj.Z.W;
		data[80] = _prevViewProj.W.X; data[81] = _prevViewProj.W.Y; data[82] = _prevViewProj.W.Z; data[83] = _prevViewProj.W.W;
		data[84] = _sunColor.X; data[85] = _sunColor.Y; data[86] = _sunColor.Z; data[87] = _sunIntensity;
		data[88] = _giStrength; data[89] = _volDensity; data[90] = _giSamples; data[91] = _reflSamples;

		byte[] bytes = new byte[368];
		Buffer.BlockCopy(data, 0, bytes, 0, 368);
		_rd!.BufferUpdate(_cameraBuffer, 0, 368, bytes);
	}

	private Rid BuildTraceUniformSet(Rid colorImage, Rid depthImage)
	{
		RDUniform reflImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
		reflImage.AddId(_reflTex);
		RDUniform giImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 26 };
		giImage.AddId(_giTex);
		RDUniform giPosImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 27 };
		giPosImage.AddId(_giPosTex);
		RDUniform giAlbedoImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 28 };
		giAlbedoImage.AddId(_giAlbedoTex);
		RDUniform giPrevPosImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 31 };
		giPrevPosImage.AddId(_giPrevPosTex);
		RDUniform giNormalImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 32 };
		giNormalImage.AddId(_giNormalTex);
		RDUniform volScatterImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 33 };
		volScatterImage.AddId(_volScatterTex);
		RDUniform giPosFullImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 36 };
		giPosFullImage.AddId(_giPosFullTex);
		RDUniform giNormalFullImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 37 };
		giNormalFullImage.AddId(_giNormalFullTex);
		RDUniform chunkPositionsPrev = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 30 };
		chunkPositionsPrev.AddId(_chunkPositionsPrevBuffer);
		RDUniform tlas = new() { UniformType = RenderingDevice.UniformType.AccelerationStructure, Binding = 1 };
		tlas.AddId(_tlas);
		RDUniform camera = new() { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 2 };
		camera.AddId(_cameraBuffer);
		RDUniform positions = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 3 };
		positions.AddId(_positionsBuffer);
		RDUniform indices = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 4 };
		indices.AddId(_indicesBuffer);
		RDUniform offsets = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 5 };
		offsets.AddId(_offsetsBuffer);
		RDUniform normals = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 6 };
		normals.AddId(_normalsBuffer);
		RDUniform materials = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 7 };
		materials.AddId(_materialBuffer);
		RDUniform colors = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 10 };
		colors.AddId(_colorBuffer);
		RDUniform emissions = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 29 };
		emissions.AddId(_emissionBuffer);
		RDUniform lightsBuf = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 34 };
		lightsBuf.AddId(_lightsBuffer);
		RDUniform prevTransforms = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 35 };
		prevTransforms.AddId(_prevTransformBuffer);
		RDUniform texParams = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 38 };
		texParams.AddId(_texParamsBuffer);
		RDUniform worldTex = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 39 };
		worldTex.AddId(_worldTexArray);
		RDUniform worldSampler = new() { UniformType = RenderingDevice.UniformType.Sampler, Binding = 40 };
		worldSampler.AddId(_worldSampler);
		RDUniform dataImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 11 };
		dataImage.AddId(_dataTex);
		RDUniform sceneColor = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 8 };
		sceneColor.AddId(colorImage);
		RDUniform sceneDepth = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 9 };
		sceneDepth.AddId(depthImage);
		RDUniform depthSampler = new() { UniformType = RenderingDevice.UniformType.Sampler, Binding = 12 };
		depthSampler.AddId(_sampler);
		RDUniform chunkPositions = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 13 };
		chunkPositions.AddId(_chunkPositionsBuffer);
		RDUniform chunkNormals = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 14 };
		chunkNormals.AddId(_chunkNormalsBuffer);
		RDUniform chunkIndices = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 15 };
		chunkIndices.AddId(_chunkIndicesBuffer);
		RDUniform chunkOffsets = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 16 };
		chunkOffsets.AddId(_chunkOffsetsBuffer);
		RDUniform chunkColors = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 17 };
		chunkColors.AddId(_chunkColorsBuffer);
		RDUniform chunkUvs = new() { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 18 };
		chunkUvs.AddId(_chunkUvsBuffer);

		Rid[] texRds = new Rid[TexSlots];
		lock (_texLock)
		{
			for (int i = 0; i < TexSlots; i++)
			{
				texRds[i] = _texRds[i].IsValid ? _texRds[i] : _whiteTex;
			}
		}
		RDUniform tex0 = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 19 };
		tex0.AddId(texRds[0]);
		RDUniform tex1 = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 20 };
		tex1.AddId(texRds[1]);
		RDUniform tex2 = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 21 };
		tex2.AddId(texRds[2]);
		RDUniform tex3 = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 22 };
		tex3.AddId(texRds[3]);
		RDUniform tex4 = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 23 };
		tex4.AddId(texRds[4]);
		RDUniform tex5 = new() { UniformType = RenderingDevice.UniformType.Texture, Binding = 24 };
		tex5.AddId(texRds[5]);
		RDUniform texSampler = new() { UniformType = RenderingDevice.UniformType.Sampler, Binding = 25 };
		texSampler.AddId(_texSampler);

		return UniformSetCacheRD.GetCache(_shader, 0, new Godot.Collections.Array<RDUniform> { reflImage, giImage, giPosImage, giAlbedoImage, giPrevPosImage, giNormalImage, volScatterImage, giPosFullImage, giNormalFullImage, chunkPositionsPrev, tlas, camera, positions, indices, offsets, normals, materials, colors, dataImage, sceneColor, sceneDepth, depthSampler, chunkPositions, chunkNormals, chunkIndices, chunkOffsets, chunkColors, chunkUvs, tex0, tex1, tex2, tex3, tex4, tex5, texSampler, emissions, lightsBuf, prevTransforms, texParams, worldTex, worldSampler });
	}

	private Rid BuildAtrousUniformSet(Rid src, Rid dst)
	{
		RDUniform colIn = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
		colIn.AddId(src);
		RDUniform colOut = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
		colOut.AddId(dst);
		RDUniform giPos = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 2 };
		giPos.AddId(_giPosTex);
		RDUniform giNormal = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 3 };
		giNormal.AddId(_giNormalTex);

		return UniformSetCacheRD.GetCache(_atrousShader, 0, new Godot.Collections.Array<RDUniform> { colIn, colOut, giPos, giNormal });
	}

	private static byte[] BuildAtrousPushConstant(Vector2I size, uint step)
	{
		int[] values = { size.X, size.Y, (int)step, 0 };
		byte[] bytes = new byte[16];
		Buffer.BlockCopy(values, 0, bytes, 0, 16);
		return bytes;
	}

	private Rid BuildReflAtrousUniformSet(Rid src, Rid dst)
	{
		RDUniform reflIn = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
		reflIn.AddId(src);
		RDUniform reflOut = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
		reflOut.AddId(dst);
		RDUniform giPos = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 2 };
		giPos.AddId(_giPosFullTex);
		RDUniform giNormal = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 3 };
		giNormal.AddId(_giNormalFullTex);
		RDUniform dataImg = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 4 };
		dataImg.AddId(_dataTex);
		return UniformSetCacheRD.GetCache(_reflAtrousShader, 0, new Godot.Collections.Array<RDUniform> { reflIn, reflOut, giPos, giNormal, dataImg });
	}

	private Rid BuildCompositeUniformSet(Rid colorImage, Rid giSource)
	{
		RDUniform color = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
		color.AddId(colorImage);
		RDUniform reflImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
		reflImage.AddId(_reflTex);
		RDUniform giImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 3 };
		giImage.AddId(giSource);
		RDUniform giAlbedoImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 4 };
		giAlbedoImage.AddId(_giAlbedoTex);
		RDUniform giPosImage = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 5 };
		giPosImage.AddId(_giPosFullTex);
		RDUniform volScatter = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 7 };
		volScatter.AddId(_volScatterTex);
		RDUniform giNormalFull = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 8 };
		giNormalFull.AddId(_giNormalFullTex);
		RDUniform giPosHalf = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 9 };
		giPosHalf.AddId(_giPosTex);
		RDUniform giNormalHalf = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 10 };
		giNormalHalf.AddId(_giNormalTex);

		return UniformSetCacheRD.GetCache(_denoiseShader, 0, new Godot.Collections.Array<RDUniform> { color, reflImage, giImage, giAlbedoImage, giPosImage, volScatter, giNormalFull, giPosHalf, giNormalHalf });
	}

	private Rid BuildTemporalUniformSet()
	{
		RDUniform giCur = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
		giCur.AddId(_giTex);
		RDUniform giHist = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
		giHist.AddId(_giHistoryTex);
		RDUniform giPos = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 2 };
		giPos.AddId(_giPosTex);
		RDUniform giOut = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 3 };
		giOut.AddId(_giDenoisedTex);
		RDUniform camera = new() { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 4 };
		camera.AddId(_cameraBuffer);
		RDUniform giPosPrev = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 5 };
		giPosPrev.AddId(_giPosPrevTex);
		RDUniform giPrevPos = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 6 };
		giPrevPos.AddId(_giPrevPosTex);
		RDUniform giNormal = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 7 };
		giNormal.AddId(_giNormalTex);

		return UniformSetCacheRD.GetCache(_temporalShader, 0, new Godot.Collections.Array<RDUniform> { giCur, giHist, giPos, giOut, camera, giPosPrev, giPrevPos, giNormal });
	}

	private byte[] BuildTemporalPushConstant(Projection vp, Vector2I size)
	{
		float[] m = new float[16];
		m[0] = vp.X.X; m[1] = vp.X.Y; m[2] = vp.X.Z; m[3] = vp.X.W;
		m[4] = vp.Y.X; m[5] = vp.Y.Y; m[6] = vp.Y.Z; m[7] = vp.Y.W;
		m[8] = vp.Z.X; m[9] = vp.Z.Y; m[10] = vp.Z.Z; m[11] = vp.Z.W;
		m[12] = vp.W.X; m[13] = vp.W.Y; m[14] = vp.W.Z; m[15] = vp.W.W;
		byte[] bytes = new byte[80];
		Buffer.BlockCopy(m, 0, bytes, 0, 64);
		int[] sz = { size.X, size.Y, 0, 0 };
		Buffer.BlockCopy(sz, 0, bytes, 64, 16);
		return bytes;
	}
}
