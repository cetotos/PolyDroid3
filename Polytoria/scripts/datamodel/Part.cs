// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Shared;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Part : Entity
{
	private MeshInstance3D? _mesh;
	private CollisionShape3D _collider = null!;
	private Material _meshMaterial = null!;
	private ShapeEnum _shape;
	private PartMaterialEnum _material;
	private Color _color = new(1, 1, 1);
	private bool _isSeperateMesh = false;
	private bool _castShadows;
	private Timer? _seperatedTimer;

	private Node3D _nRemoteAt = null!; // Remote collider proxy

	public bool IsMeshSeperated => _isSeperateMesh;
	public int BridgeID = -1;

	private Vector3 _partSize = Vector3.One;

	// NOTE: Part size is local
	internal Vector3 PartSize
	{
		get => _partSize;
		set
		{
			_partSize = value;
			_mesh?.Scale = _partSize;
			_nRemoteAt?.Scale = _partSize;
		}
	}

	public override void EnterTree()
	{
		Instance? current = Parent;
		while (current != null)
		{
			if (current is UIViewport)
			{
				OverrideNoMultiMesh = true;
				CreateSeperateMesh();
			}
			current = current.Parent;
		}

		base.EnterTree();
	}

	public override void Init()
	{
		base.Init();
		GDNode3D.AddChild(_collider = new(), false);
		GDNode3D.AddChild(_nRemoteAt = new(), false);
		_collider.SetMeta("_remote_at", _nRemoteAt);
		_nRemoteAt.Rotation = Vector3.Zero;

		if (OS.HasFeature("debug-face"))
		{
			RayCast3D raycast = new()
			{
				TargetPosition = new(0, 0, 2)
			};
			GDNode3D.AddChild(raycast);
		}

		Shape = this is Truss ? ShapeEnum.Truss : ShapeEnum.Brick;
	}

	public override void PreDelete()
	{
		RemoveCollisionShape(_collider);

		if (GodotObject.IsInstanceValid(_meshMaterial))
			_meshMaterial.Dispose();

		base.PreDelete();
	}

	public override void Ready()
	{
		AddCollisionShape(_collider);
		UpdateCollision();

#if CREATOR
		if (Root.Network.NetworkMode == Services.NetworkService.NetworkModeEnum.Creator)
		{
			OverrideNoMultiMesh = true;
			CreateSeperateMesh();
		}
#endif
		base.Ready();
	}

	public void CreateSeperateMesh()
	{
		if (_isSeperateMesh)
		{
			return;
		}
		_isSeperateMesh = true;
		if (Root != null && Root.Bridge != null)
		{
			Root.Bridge.SeparatedPartCount++;
		}
		GDNode3D.AddChild(_mesh = new(), false);
		_meshMaterial = new StandardMaterial3D();

		// Disabling this cuz it creates a mess as of now
		/*
		if (Root.Bridge != null)
		{
			if (_seperatedTimer != null)
			{
				_seperatedTimer.QueueFree();
				_seperatedTimer = null;
			}

			_seperatedTimer = new();
			GDNode3D.AddChild(_seperatedTimer, false);
			_seperatedTimer.Timeout += OnDMBTimeout;
			_seperatedTimer.Start(DMBTimeout);
		}
		*/

		_mesh.Scale = _partSize;

		UpdateShape();
		UpdateMaterial();
		UpdateColor();
		UpdateShadow();
		RefreshUV1();
	}

	private void OnDMBTimeout()
	{
		_seperatedTimer?.QueueFree();
		_seperatedTimer = null;

		Root.Bridge.AddPart(this);
		RemoveSeperateMesh();
	}

	public void RemoveSeperateMesh()
	{
		if (!_isSeperateMesh)
		{
			return;
		}
		_isSeperateMesh = false;
		Root.Bridge.SeparatedPartCount--;
		_mesh?.Free();
	}

	[Editable, ScriptProperty, DefaultValue(ShapeEnum.Brick)]
	public ShapeEnum Shape
	{
		get => _shape;
		set
		{
			_shape = value;

			UpdateShape();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(PartMaterialEnum.SmoothPlastic)]
	public PartMaterialEnum Material
	{
		get => _material;
		set
		{
			_material = value;

			UpdateMaterial();
			RefreshUV1();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public override Color Color
	{
		get => _color;
		set
		{
			_color = value;
			//GD.PushWarning("Set color: ", _color);

			UpdateColor();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public override bool CastShadows
	{
		get => _castShadows;
		set
		{
			_castShadows = value;

			UpdateShadow();
			OnPropertyChanged();
		}
	}

	// Override this to be excluded from MutliMesh
	internal bool OverrideNoMultiMesh = false;

	internal void RefreshUV1()
	{
		if (_isSeperateMesh && _meshMaterial is StandardMaterial3D sm)
		{
			sm.Uv1Scale = Size / 4;
		}
	}

	internal void UpdateShape()
	{
		if (_collider == null) return;
		(Godot.Mesh mesh, Shape3D shape) = Globals.LoadShape(_shape.ToString());
		if (_isSeperateMesh)
		{
			_mesh?.Mesh = mesh;
			_collider.Shape = shape;
		}
		else
		{
			_collider.Shape = shape;
		}
		PostCollisionShapeUpdate(_collider);
	}

	internal void UpdateMaterial()
	{
		if (_isSeperateMesh)
		{
			Material temp = Globals.LoadMaterial(_material.ToString());
			if (temp is StandardMaterial3D mat)
			{
				if (_meshMaterial is StandardMaterial3D sm)
				{
					mat.RoughnessTexture = null;
					mat.RenderPriority = 5;
					mat.AlbedoColor = sm.AlbedoColor;
					mat.Emission = sm.Emission;
					mat.Transparency = sm.Transparency;
				}
			}
			_mesh?.MaterialOverride = _meshMaterial = temp;
		}
	}

	internal void UpdateColor()
	{
		if (_isSeperateMesh)
		{
			if (_meshMaterial is StandardMaterial3D sm)
			{
				sm.AlbedoColor = _color;
				sm.Emission = _color;
				sm.Transparency = _color.A == 1 ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha;
			}
		}

		UpdateCamLayer();
	}

	internal void UpdateShadow()
	{
		if (_isSeperateMesh)
		{
			_mesh?.CastShadow = _castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
		}
	}

	public override Aabb GetSelfBound()
	{
		Transform3D t = GetGlobalTransform();

		Vector3 localSize = Size;
		Vector3 he = localSize / 2f;

		Vector3 basisScale = t.Basis.Scale;

		// get pure rotation matrix
		Basis rot = t.Basis;
		rot.X /= basisScale.X;
		rot.Y /= basisScale.Y;
		rot.Z /= basisScale.Z;

		// some dark magic
		Vector3 worldExtents = new(
			Mathf.Abs(rot.X.X) * he.X + Mathf.Abs(rot.Y.X) * he.Y + Mathf.Abs(rot.Z.X) * he.Z,
			Mathf.Abs(rot.X.Y) * he.X + Mathf.Abs(rot.Y.Y) * he.Y + Mathf.Abs(rot.Z.Y) * he.Z,
			Mathf.Abs(rot.X.Z) * he.X + Mathf.Abs(rot.Y.Z) * he.Y + Mathf.Abs(rot.Z.Z) * he.Z
		);

		Vector3 center = t.Origin;

		return new(center - worldExtents, worldExtents * 2);
	}

	public enum ShapeEnum
	{
		Brick = 0,
		Sphere = 1,
		Cylinder = 2,
		Cone = 3,
		Wedge = 4,
		Corner = 5,
		Bevel = 6,
		Concave = 7,
		Truss = 8,
		Frame = 9
	}

	[Attributes.Obsolete("This should not be used, it's here only for compatibility with legacy scripts.")]
	public enum LegacyShapeEnum
	{
		Brick = 0,
		Ball = 1,
		Cylinder = 2,
		Wedge = 4,
		Truss = 8,
		TrussFrame = 9,
		Bevel = 6,
		QuarterPipe = 7,
		Cone = 3,
		CornerWedge = 5,
	}

	[CreatorEnumOptions(SortOption = EnumSortOption.Alphabetical)]
	public enum PartMaterialEnum
	{
		SmoothPlastic,
		Brick,
		Concrete,
		Dirt,
		Fabric,
		Grass,
		Ice,
		Marble,
		Metal,
		MetalGrid,
		MetalPlate,
		Neon,
		Planks,
		Plastic,
		Plywood,
		RustyIron,
		Sand,
		Sandstone,
		Snow,
		Stone,
		Wood
	}
}
