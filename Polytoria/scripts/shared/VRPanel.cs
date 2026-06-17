// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class VRPanel : Node3D, IVRPointerTarget
{
	private const float DistanceMeters = 2.5f;
	private const float FollowSpeed = 4f;
	private const float IdleFadeAfterSeconds = 4f;
	private const float FadeSpeed = 6f;
	internal const float WidthMeters = 2.25f;
	internal static readonly Vector2I PanelSize = new(1920, 1080);

	private SubViewport _subViewport = null!;
	private MeshInstance3D _quad = null!;
	private QuadMesh _quadMesh = null!;
	private StandardMaterial3D _mat = null!;
	private OpenXRCompositionLayerQuad? _layer;
	private bool _layerActive;
	private XRCamera3D? _camera;
	private float _lastHitTime = -100f;
	private float _alpha = 1f;
	private bool _maximized = true;
	private float _lastWorldScale = -1f;

	public static VRPanel? Instance { get; private set; }
	public SubViewport Viewport => _subViewport;
	public bool IsMaximized => _maximized;

	public Transform3D PanelGlobalTransform => GlobalTransform;
	public Vector2 PanelSizeMeters
	{
		get
		{
			float ws = (float)XRServer.WorldScale;
			if (ws <= 0f) ws = 1f;
			return new Vector2(WidthMeters * ws, WidthMeters * (float)PanelSize.Y / PanelSize.X * ws);
		}
	}
	public Vector2I ViewportPixelSize => _subViewport.Size;
	public SubViewport TargetViewport => _subViewport;
	public bool AcceptsPointer => _maximized && _alpha > 0.05f;

	public override void _Ready()
	{
		Instance = this;

		_subViewport = new SubViewport
		{
			Size = PanelSize,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			TransparentBg = true,
			HandleInputLocally = true,
			GuiEmbedSubwindows = true,
		};
		AddChild(_subViewport);

		_quadMesh = new QuadMesh();
		_mat = new StandardMaterial3D
		{
			AlbedoTexture = _subViewport.GetTexture(),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		_quad = new MeshInstance3D { Mesh = _quadMesh, MaterialOverride = _mat };
		AddChild(_quad);

		VRPointerRegistry.Register(this);

		GetTree().NodeAdded += OnNodeAdded;
		CallDeferred(nameof(AbsorbExistingCanvasLayers));
	}

	private void OnNodeAdded(Node node)
	{
		if (node is not CanvasLayer) return;
		Callable.From(() => TryAbsorbLate(node)).CallDeferred();
	}

	private void TryAbsorbLate(Node node)
	{
		if (node is not CanvasLayer layer || !IsInstanceValid(layer) || !layer.IsInsideTree()) return;
		if (!IsInsideTree() || layer.GetViewport() != GetTree().Root) return;
		AbsorbCanvasLayer(layer);
	}

	private void AbsorbExistingCanvasLayers()
	{
		Node? sceneRoot = GetTree()?.Root;
		if (sceneRoot == null) return;
		CollectAndReparent(sceneRoot);
	}

	private void CollectAndReparent(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is SubViewport) continue;
			if (child == this) continue;
			if (IsAncestorOf(child)) continue;
			if (child is CanvasLayer cl)
			{
				if (cl.GetParent() == _subViewport) continue;
				cl.Reparent(_subViewport);
				continue;
			}
			CollectAndReparent(child);
		}
	}

	public void AbsorbCanvasLayer(CanvasLayer layer)
	{
		if (layer == null || _subViewport == null) return;
		if (layer.GetParent() == _subViewport) return;
		if (IsAncestorOf(layer)) return;
		layer.Reparent(_subViewport);
	}

	public void SetCamera(XRCamera3D cam)
	{
		_camera = cam;
		SpawnLayer();
	}

	private void SpawnLayer()
	{
		if (_camera?.GetParent() is not XROrigin3D origin) return;

		if (_layer != null && IsInstanceValid(_layer))
		{
			if (_layer.GetParent() != origin)
			{
				_layer.Reparent(origin);
			}
			return;
		}

		_layer = new OpenXRCompositionLayerQuad
		{
			LayerViewport = _subViewport,
			SortOrder = 1,
			AlphaBlend = true,
			Visible = false,
		};
		origin.AddChild(_layer);
	}

	private void UpdateLayer()
	{
		if (_layer == null) return;
		if (!IsInstanceValid(_layer))
		{
			_layer = null;
			_layerActive = false;
			_quad.Visible = true;
			return;
		}
		if (!_layerActive)
		{
			if (!_layer.IsInsideTree()) return;
			if (!_layer.IsNativelySupported())
			{
				_layer.QueueFree();
				_layer = null;
				return;
			}
			_layerActive = true;
			_quad.Visible = false;
		}
		_layer.Visible = _maximized && _alpha > 0.05f;
		_layer.QuadSize = PanelSizeMeters;
		_layer.GlobalTransform = GlobalTransform;
	}

	public void SetMaximized(bool max)
	{
		_maximized = max;
		Visible = max;
		if (max) _lastHitTime = (float)Time.GetTicksMsec() / 1000f;
	}

	public void OnPointerHit()
	{
		_lastHitTime = (float)Time.GetTicksMsec() / 1000f;
	}

	public override void _Process(double delta)
	{
		Vector2I scaledSize = (Vector2I)((Vector2)PanelSize / VRSettings.UiScale);
		if (_subViewport.Size != scaledSize)
		{
			_subViewport.Size = scaledSize;
		}

		float targetAlpha = _maximized ? 1f : 0f;
		_alpha = Mathf.MoveToward(_alpha, targetAlpha, (float)delta * FadeSpeed);
		_mat.AlbedoColor = new Color(1f, 1f, 1f, _alpha);

		if (!_maximized || _camera == null)
		{
			UpdateLayer();
			return;
		}

		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		if (Mathf.Abs(ws - _lastWorldScale) > 0.001f)
		{
			float aspect = (float)PanelSize.Y / PanelSize.X;
			_quadMesh.Size = new Vector2(WidthMeters * ws, WidthMeters * aspect * ws);
			_lastWorldScale = ws;
		}
		float distance = DistanceMeters * ws;

		Transform3D camGlobal = _camera.GlobalTransform;
		Vector3 forwardFlat = -camGlobal.Basis.Z;
		forwardFlat.Y = 0;
		if (forwardFlat.LengthSquared() < 1e-4f) forwardFlat = Vector3.Forward;
		forwardFlat = forwardFlat.Normalized();
		Vector3 target = camGlobal.Origin + forwardFlat * distance;
		target.Y = camGlobal.Origin.Y;

		float t = (float)(delta * FollowSpeed);
		GlobalPosition = GlobalPosition.Lerp(target, t);

		Vector3 lookAt = new(camGlobal.Origin.X, GlobalPosition.Y, camGlobal.Origin.Z);
		if (GlobalPosition.DistanceSquaredTo(lookAt) > 0.01f)
		{
			LookAt(lookAt, Vector3.Up, true);
		}

		UpdateLayer();
	}

	public override void _ExitTree()
	{
		if (GetTree() != null)
		{
			GetTree().NodeAdded -= OnNodeAdded;
		}
		if (_layer != null && IsInstanceValid(_layer))
		{
			_layer.QueueFree();
			_layer = null;
		}
		VRPointerRegistry.Unregister(this);
		if (Instance == this) Instance = null;
	}
}
