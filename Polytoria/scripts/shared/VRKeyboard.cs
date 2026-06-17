// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class VRKeyboard : Node3D, IVRPointerTarget
{
	private const string SceneRes = "res://addons/xr_keyboard/virtual_keyboard_2d.tscn";
	private const float WidthMeters = 1.0f;
	private const float DownOffsetMeters = 0.62f;
	private const float ForwardOffsetMeters = 0.75f;
	private const float TiltDegrees = -30f;
	private const float FollowSpeed = 2f;
	private const float FollowDeadZoneMeters = 0.18f;
	private const int ContentScale = 12;
	private const int HitFrames = 8;
	private const int HeaderHeightUnscaled = 28;
	private static readonly Vector2I PanelSize = new(400 * ContentScale, (200 + HeaderHeightUnscaled) * ContentScale);

	private SubViewport _viewport = null!;
	private MeshInstance3D _quad = null!;
	private QuadMesh _quadMesh = null!;
	private StandardMaterial3D _mat = null!;
	private CanvasLayer _keyboardScene = null!;
	private SubViewport _targetViewport = null!;
	private XRCamera3D? _camera;
	private float _lastWorldScale = -1f;
	private bool _shown;
	private int _dirtyFrames;

	public static VRKeyboard? Instance { get; private set; }

	public Transform3D PanelGlobalTransform => _quad.GlobalTransform;
	public Vector2 PanelSizeMeters => _quadMesh.Size;
	public Vector2I ViewportPixelSize => PanelSize;
	public SubViewport TargetViewport => _viewport;
	public bool AcceptsPointer => _shown && Visible;

	public VRKeyboard(SubViewport targetViewport, XRCamera3D camera)
	{
		_targetViewport = targetViewport;
		_camera = camera;
	}

	public void SetCamera(XRCamera3D cam) => _camera = cam;

	public override void _Ready()
	{
		Instance = this;
		Visible = false;

		_viewport = new SubViewport
		{
			Size = PanelSize,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
			TransparentBg = true,
			HandleInputLocally = true,
		};
		AddChild(_viewport);
		_dirtyFrames = 3;

		_quadMesh = new QuadMesh();
		_mat = new StandardMaterial3D
		{
			AlbedoTexture = _viewport.GetTexture(),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
		};
		_quad = new MeshInstance3D
		{
			Mesh = _quadMesh,
			MaterialOverride = _mat,
			RotationDegrees = new Vector3(TiltDegrees, 0, 0),
		};
		AddChild(_quad);

		int headerPx = HeaderHeightUnscaled * ContentScale;

		var headerBg = new ColorRect
		{
			Color = new Color(0.08f, 0.08f, 0.1f, 0.95f),
			Size = new Vector2(PanelSize.X, headerPx),
			Position = Vector2.Zero,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_viewport.AddChild(headerBg);

		PackedScene packed = GD.Load<PackedScene>(SceneRes);
		_keyboardScene = (CanvasLayer)packed.Instantiate();
		_keyboardScene.Set("target_viewport", _targetViewport);
		_keyboardScene.Transform = new Transform2D(
			new Vector2(ContentScale, 0),
			new Vector2(0, ContentScale),
			new Vector2(0, headerPx)
		);
		_viewport.AddChild(_keyboardScene);

		var closeBtn = new Button
		{
			Text = "X",
			Size = new Vector2(headerPx - 4 * ContentScale, headerPx - 4 * ContentScale),
			Position = new Vector2(PanelSize.X - headerPx + 2 * ContentScale, 2 * ContentScale),
			FocusMode = Control.FocusModeEnum.None,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		closeBtn.AddThemeFontSizeOverride("font_size", (HeaderHeightUnscaled - 12) * ContentScale);
		closeBtn.Pressed += () => SetShown(false);
		_viewport.AddChild(closeBtn);

		_keyboardScene.Connect("key_pressed", Callable.From((string _scan, int _unicode, bool _shift) =>
		{
			XRHaptics.Pulse(XRControlBridge.DominantController, 0.5f, 0.02f);
		}));

		_targetViewport.GuiFocusChanged += OnTargetFocusChanged;
		VRPointerRegistry.Register(this);
	}

	public override void _Process(double delta)
	{
		if (_dirtyFrames > 0)
		{
			_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
			_dirtyFrames--;
		}

		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		if (Mathf.Abs(ws - _lastWorldScale) > 0.001f)
		{
			float aspect = (float)PanelSize.Y / PanelSize.X;
			_quadMesh.Size = new Vector2(WidthMeters * ws, WidthMeters * aspect * ws);
			_lastWorldScale = ws;
		}

		if (!_shown || _camera == null) return;

		Transform3D camGlobal = _camera.GlobalTransform;
		Vector3 forwardFlat = -camGlobal.Basis.Z;
		forwardFlat.Y = 0f;
		if (forwardFlat.LengthSquared() < 1e-4f) forwardFlat = Vector3.Forward;
		forwardFlat = forwardFlat.Normalized();

		Vector3 target = camGlobal.Origin + forwardFlat * (ForwardOffsetMeters * ws);
		target.Y = camGlobal.Origin.Y - DownOffsetMeters * ws;

		float deadZone = FollowDeadZoneMeters * ws;
		if (GlobalPosition.DistanceSquaredTo(target) < deadZone * deadZone) return;

		float t = (float)(delta * FollowSpeed);
		GlobalPosition = GlobalPosition.Lerp(target, t);

		Vector3 lookAt = new(camGlobal.Origin.X, GlobalPosition.Y, camGlobal.Origin.Z);
		if (GlobalPosition.DistanceSquaredTo(lookAt) > 0.01f)
		{
			LookAt(lookAt, Vector3.Up, true);
		}
	}

	public void OnPointerHit() => _dirtyFrames = HitFrames;

	private void OnTargetFocusChanged(Control owner)
	{
		bool wantsKeyboard = owner switch
		{
			LineEdit le => le.VirtualKeyboardEnabled,
			TextEdit te => te.VirtualKeyboardEnabled,
			_ => false,
		};
		SetShown(wantsKeyboard);
	}

	private void SetShown(bool show)
	{
		if (show == _shown) return;
		_shown = show;
		Visible = show;
		if (show) _dirtyFrames = 3;
	}

	public override void _ExitTree()
	{
		VRPointerRegistry.Unregister(this);
		if (_targetViewport != null)
		{
			_targetViewport.GuiFocusChanged -= OnTargetFocusChanged;
		}
		if (Instance == this) Instance = null;
	}
}
