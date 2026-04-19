// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;

namespace Polytoria.Client;

/// <summary>
/// Multimesh bridge for Datamodel
/// </summary>
public partial class DatamodelBridge : Node3D
{
	private const float ChunkBaseSize = 64f;
	private const uint MaxScaleLevel = 10;
	private const uint DynamicChunkScaleFactor = 1; // dynamic parts have a larger chunk size because they cross chunk boundaries more often than static parts

	private World Root = null!;
	public long SeparatedPartCount = 0;

	private Dictionary<Part, PartHandle> _handles = [];
	private Dictionary<Part, PartSubscription> _subscriptions = [];
	private Dictionary<BatchKey, ChunkBatch> _batches = [];
	private HashSet<Part> _dirty = [];
	private HashSet<Part> _dynamics = [];
	private Rid _scenario;

	private Dictionary<(Part.PartMaterialEnum, bool), Material> _materials = [];

	private bool isGameReady = false;

	public void Attach(World root)
	{
		if (Root != null)
		{
			Root.InstanceEnteredTree -= OnInstanceAdded;
			Root.InstanceExitingTree -= OnInstanceRemoving;
		}

		Root = root;
		root.Bridge = this;

		_scenario = Root.World3D.Scenario;

		root.InstanceEnteredTree += OnInstanceAdded;
		root.InstanceExitingTree += OnInstanceRemoving;
		root.Loaded.Once(OnGameReady);
	}

	public override void _ExitTree()
	{
		if (Root != null)
		{
			Root.InstanceEnteredTree -= OnInstanceAdded;
			Root.InstanceExitingTree -= OnInstanceRemoving;
			Root.Loaded.Disconnect(OnGameReady);
			Root.Bridge = null!;
		}
		base._ExitTree();
	}

	private Material GetMaterial(Part.PartMaterialEnum partMaterial, bool isTransparent)
	{
		if (_materials.TryGetValue((partMaterial, isTransparent), out Material? mat))
		{
			return mat;
		}

		mat = Globals.LoadMaterial(partMaterial, isTransparent ? 0f : 1f);
		if (mat == null)
		{
			throw new System.Exception("Unknown material: " + partMaterial.ToString());
		}

		if (mat is StandardMaterial3D sm)
		{
			sm.VertexColorUseAsAlbedo = true;
			sm.VertexColorIsSrgb = true;
			sm.Uv1WorldTriplanar = true;

			if (isTransparent)
			{
				sm.Transparency = isTransparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
			}

			sm.RoughnessTexture = null;

			// Disable some property for mobile for performance
#if GODOT_MOBILE
			sm.NormalTexture = null;
			sm.DetailEnabled = false;
			sm.AOTexture = null;
#endif
		}

		_materials.Add((partMaterial, isTransparent), mat);

		return mat;
	}

	public override void _Process(double delta)
	{
		if (!isGameReady) return;
		if (_dirty.Count == 0) return;

		Part[] dirtyParts = [.. _dirty];
		_dirty.Clear();

		foreach (Part part in dirtyParts)
		{
			bool inBatch = _handles.TryGetValue(part, out PartHandle? handle);
			bool shouldBatch = IsPartEligible(part);

			if (!shouldBatch)
			{
				if (inBatch)
				{
					RemoveFromBatch(part);
				}

				if (!part.IsMeshSeparated)
				{
					part.CreateSeparateMesh();
				}

				continue;
			}

			BatchKey newKey = GetKeyForPart(part);

			if (!inBatch)
			{
				AddToBatch(part, newKey);
				continue;
			}

			if (!newKey.Equals(handle!.Key))
			{
				RemoveFromBatch(part);
				AddToBatch(part, newKey);
				continue;
			}

			if (_batches.TryGetValue(handle.Key, out ChunkBatch? batch))
			{
				UpdateInstanceData(batch, handle, part, !handle.Key.IsDynamic);
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!isGameReady) return;
		if (_dynamics.Count == 0) return;

		Part[] dynamics = [.. _dynamics];

		foreach (Part part in dynamics)
		{
			if (part.IsDeleted) continue;
			if (!_handles.TryGetValue(part, out PartHandle? handle)) continue;
			if (!handle.Key.IsDynamic) continue;
			if (!_batches.TryGetValue(handle.Key, out ChunkBatch? batch)) continue;

			BatchKey newKey = GetKeyForPart(part);

			if (!newKey.Equals(handle.Key))
			{
				_dirty.Add(part);
				continue;
			}

			Transform3D transform = part.GetGlobalTransform();
			Color color = part.Color.SrgbToLinear();
			Color customData = GetCustomDataForPart(part);

			bool transformChanged = !handle.LastTransform.IsEqualApprox(transform);
			bool colorChanged = !handle.LastColor.IsEqualApprox(color);
			bool customChanged = !handle.LastCustomData.IsEqualApprox(customData);

			if (!transformChanged && !colorChanged && !customChanged)
			{
				continue;
			}

			if (transformChanged)
			{
				batch.MultiMesh.SetInstanceTransform(handle.Index, transform);
			}

			if (colorChanged)
			{
				batch.MultiMesh.SetInstanceColor(handle.Index, color);
			}

			if (customChanged)
			{
				batch.MultiMesh.SetInstanceCustomData(handle.Index, customData);
			}

			handle.LastTransform = transform;
			handle.LastColor = color;
			handle.LastCustomData = customData;
		}
	}

	private static void UpdateInstanceData(ChunkBatch batch, PartHandle handle, Part part, bool updateTransform = true)
	{
		Transform3D transform = part.GetGlobalTransform();
		Color color = part.Color.SrgbToLinear();
		Color customData = GetCustomDataForPart(part);

		if (updateTransform)
		{
			batch.MultiMesh.SetInstanceTransform(handle.Index, transform);
		}

		batch.MultiMesh.SetInstanceColor(handle.Index, color);
		batch.MultiMesh.SetInstanceCustomData(handle.Index, customData);

		handle.LastTransform = transform;
		handle.LastColor = color;
		handle.LastCustomData = customData;
	}

	private static BatchKey GetKeyForPart(Part part)
	{
		bool isDynamic = !part.Anchored;

		uint scaleLevel = 1;
		float size = ChunkBaseSize;

		while (part.Size.X > size || part.Size.Y > size || part.Size.Z > size)
		{
			size *= 2;
			scaleLevel++;

			if (scaleLevel > MaxScaleLevel) break;
		}

		if (isDynamic)
		{
			scaleLevel = Math.Min(MaxScaleLevel, scaleLevel + DynamicChunkScaleFactor);
		}


		Vector3I coord = GetChunkCoord(part.Position, scaleLevel);

		return new BatchKey
		{
			Coord = coord,
			Material = part.Material,
			Shape = part.Shape,
			IsTransparent = part.Color.A < 1f,
			CastShadows = part.CastShadows,
			ScaleLevel = scaleLevel,
			IsDynamic = isDynamic
		};
	}

	private static Vector3I GetChunkCoord(Vector3 pos, uint scaleLevel = 1)
	{
		float size = ChunkBaseSize * Mathf.Pow(2, scaleLevel - 1);

		int cx = Mathf.FloorToInt(pos.X / size);
		int cy = Mathf.FloorToInt(pos.Y / size);
		int cz = Mathf.FloorToInt(pos.Z / size);

		return new Vector3I(cx, cy, cz);
	}

	private void OnInstanceAdded(Instance instance)
	{
		if (instance is Part part)
		{
			AddPart(part);
		}
	}

	private void OnInstanceRemoving(Instance instance)
	{
		if (instance is Part part)
		{
			RemovePart(part);
		}
	}

	private void OnGameReady()
	{
		isGameReady = true;
	}

	private void AddToBatch(Part part, BatchKey key)
	{
		if (!_batches.TryGetValue(key, out var batch))
		{
			(Godot.Mesh mesh, _) = Globals.LoadShape(part.Shape.ToString());

			MultiMesh mm = new()
			{
				Mesh = mesh,
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true,
				UseCustomData = true,
				InstanceCount = 64,
				VisibleInstanceCount = 0
			};

			Rid rid = RenderingServer.InstanceCreate();
			RenderingServer.InstanceSetScenario(rid, _scenario);
			RenderingServer.InstanceSetBase(rid, mm.GetRid());
			RenderingServer.InstanceSetTransform(rid, Transform3D.Identity);
			RenderingServer.InstanceGeometrySetCastShadowsSetting(rid, key.CastShadows ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off);

			Material mat = GetMaterial(part.Material, part.Color.A < 1f);
			RenderingServer.InstanceGeometrySetMaterialOverride(rid, mat.GetRid());

			batch = new ChunkBatch
			{
				Key = key,
				MultiMesh = mm,
				Rid = rid,
				Parts = new List<Part>(64),
				Count = 0
			};

			_batches.Add(key, batch);
		}

		part.RemoveSeparateMesh();

		int index = batch.Count;
		ResizeBatch(batch, index + 1);

		batch.Parts.Add(part);
		batch.Count++;
		batch.MultiMesh.VisibleInstanceCount = batch.Count;

		PartHandle handle = new()
		{
			Key = key,
			Index = index
		};

		_handles[part] = handle;

		if (key.IsDynamic)
		{
			_dynamics.Add(part);
		}
		else
		{
			_dynamics.Remove(part);
		}

		UpdateInstanceData(batch, handle, part, true);
	}

	private void RemoveFromBatch(Part part)
	{
		if (!_handles.TryGetValue(part, out PartHandle? handle)) return;
		if (!_batches.TryGetValue(handle.Key, out var batch))
		{
			_handles.Remove(part);
			_dynamics.Remove(part);
			return;
		}

		int index = handle.Index;
		int lastIndex = batch.Count - 1;

		if (index != lastIndex)
		{
			var lastPart = batch.Parts[lastIndex];
			batch.Parts[index] = lastPart;

			PartHandle movedHandle = _handles[lastPart];
			movedHandle.Index = index;
			movedHandle.Key = handle.Key;

			UpdateInstanceData(batch, movedHandle, lastPart, true);
		}

		batch.Parts.RemoveAt(lastIndex);
		batch.Count--;
		batch.MultiMesh.VisibleInstanceCount = batch.Count;

		if (batch.Count == 0)
		{
			RenderingServer.FreeRid(batch.Rid);
			_batches.Remove(handle.Key);
		}

		_handles.Remove(part);
		_dynamics.Remove(part);
	}

	private static void ResizeBatch(ChunkBatch batch, int target)
	{
		if (target <= batch.MultiMesh.InstanceCount) return;

		int oldUsedCount = batch.Count;
		int newCap = batch.MultiMesh.InstanceCount;

		while (newCap < target)
		{
			newCap *= 2;
		}

		batch.MultiMesh.InstanceCount = newCap;

		// changing instancecount wipes multimesh data
		for (int i = 0; i < oldUsedCount; i++)
		{
			var p = batch.Parts[i];
			batch.MultiMesh.SetInstanceTransform(i, p.GetGlobalTransform());
			batch.MultiMesh.SetInstanceColor(i, p.Color.SrgbToLinear());
			batch.MultiMesh.SetInstanceCustomData(i, GetCustomDataForPart(p));
		}
	}

	private static Color GetCustomDataForPart(Part part)
	{
		float emissiveStrength = part.Material == Part.PartMaterialEnum.Neon ? 2.0f : 0.0f;
		return new Color(emissiveStrength, 0f, 0f, 0f);
	}

	public void AddPart(Part part)
	{
		if (_subscriptions.ContainsKey(part)) return;

		Action<string> propertyChangedHandler = _ =>
		{
			if (!isGameReady) return;
			_dirty.Add(part);
		};

		part.PropertyChanged.Connect(propertyChangedHandler);

		_subscriptions[part] = new PartSubscription
		{
			PropertyChangedHandler = propertyChangedHandler
		};

		if (IsPartEligible(part))
		{
			AddToBatch(part, GetKeyForPart(part));
			_dirty.Add(part);
		}
		else
		{
			part.CreateSeparateMesh();
		}
	}

	public void RemovePart(Part part)
	{
		if (_subscriptions.TryGetValue(part, out PartSubscription? sub))
		{
			part.PropertyChanged.Disconnect(sub.PropertyChangedHandler);
			_subscriptions.Remove(part);
		}

		part.CreateSeparateMesh();
		RemoveFromBatch(part);

		_dirty.Remove(part);
		_dynamics.Remove(part);
	}

	public static bool IsPartEligible(Part part)
	{
		if (part.IsHidden || part.IsInTemporary) return false;
		if (part.OverrideNoMultiMesh) return false;
		if (!IsInstanceValid(part.GDNode3D) || !part.GDNode3D.IsInsideTree()) return false;
		if (part.IsDeleted) return false;

		return true;
	}

	private class PartSubscription
	{
		public Action<string> PropertyChangedHandler = null!;
	}

	private class PartHandle
	{
		public BatchKey Key;
		public int Index;

		public Transform3D LastTransform;
		public Color LastColor;
		public Color LastCustomData;
	}

	private struct BatchKey
	{
		public Vector3I Coord;
		public Part.PartMaterialEnum Material;
		public Part.ShapeEnum Shape;
		public bool IsTransparent;
		public bool CastShadows;
		public uint ScaleLevel;
		public bool IsDynamic;
	}

	private class ChunkBatch
	{
		public BatchKey Key;
		public MultiMesh MultiMesh = null!;
		public Rid Rid;
		public List<Part> Parts = [];
		public int Count;
	}
}
