// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class VRBottomBar : Node3D, IVRPointerTarget
{
	private const float DistanceMeters = 1.5f;
	private const float DownOffsetMeters = 0.55f;
	private const float FollowSpeed = 4f;
	internal const float WidthMeters = 0.36f;
	internal const float HeightMeters = 0.09f;
	internal static readonly Vector2I PanelSize = new(400, 100);

	private SubViewport _subViewport = null!;
	private MeshInstance3D _quad = null!;
	private QuadMesh _quadMesh = null!;
	private Button _toggleButton = null!;
	private XRCamera3D? _camera;
	private float _lastWorldScale = -1f;

	public static VRBottomBar? Instance { get; private set; }
	public SubViewport Viewport => _subViewport;

	public Transform3D PanelGlobalTransform => GlobalTransform;
	public Vector2 PanelSizeMeters
	{
		get
		{
			float ws = (float)XRServer.WorldScale;
			if (ws <= 0f) ws = 1f;
			return new Vector2(WidthMeters * ws, HeightMeters * ws);
		}
	}
	public Vector2I ViewportPixelSize => PanelSize;
	public SubViewport TargetViewport => _subViewport;
	public bool AcceptsPointer => true;

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

		var bg = new ColorRect
		{
			Color = new Color(0.08f, 0.08f, 0.10f, 0.85f),
			AnchorRight = 1,
			AnchorBottom = 1,
		};
		_subViewport.AddChild(bg);

		var row = new HBoxContainer
		{
			AnchorLeft = 0,
			AnchorRight = 1,
			AnchorTop = 0,
			AnchorBottom = 1,
			OffsetLeft = 8,
			OffsetRight = -8,
			OffsetTop = 8,
			OffsetBottom = -8,
		};
		row.AddThemeConstantOverride("separation", 12);
		_subViewport.AddChild(row);

		_toggleButton = MakeButton("Hide UI", OnToggle);
		row.AddChild(_toggleButton);
		RefreshButtonText();

		_quadMesh = new QuadMesh();
		var mat = new StandardMaterial3D
		{
			AlbedoTexture = _subViewport.GetTexture(),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
		};
		_quad = new MeshInstance3D { Mesh = _quadMesh, MaterialOverride = mat };
		AddChild(_quad);

		VRPointerRegistry.Register(this);
	}

	public void SetCamera(XRCamera3D cam) => _camera = cam;

	public void OnPointerHit() { }

	private static Button MakeButton(string text, System.Action onPressed)
	{
		var button = new Button
		{
			Text = text,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			FocusMode = Control.FocusModeEnum.None,
		};
		button.AddThemeFontSizeOverride("font_size", 48);
		button.Pressed += () => onPressed();
		return button;
	}

	private void OnToggle()
	{
		if (VRPanel.Instance != null)
		{
			VRPanel.Instance.SetMaximized(!VRPanel.Instance.IsMaximized);
			RefreshButtonText();
		}
	}

	public void RefreshButtonText()
	{
		if (_toggleButton == null) return;
		bool max = VRPanel.Instance?.IsMaximized ?? false;
		string desired = max ? "Hide UI" : "Show UI";
		if (_toggleButton.Text != desired) _toggleButton.Text = desired;
	}

	public override void _Process(double delta)
	{
		RefreshButtonText();
		if (_camera == null) return;

		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		if (Mathf.Abs(ws - _lastWorldScale) > 0.001f)
		{
			_quadMesh.Size = new Vector2(WidthMeters * ws, HeightMeters * ws);
			_lastWorldScale = ws;
		}
		float distance = DistanceMeters * ws;
		float downOffset = DownOffsetMeters * ws;

		Transform3D camGlobal = _camera.GlobalTransform;
		Vector3 forwardFlat = -camGlobal.Basis.Z;
		forwardFlat.Y = 0;
		if (forwardFlat.LengthSquared() < 1e-4f) forwardFlat = Vector3.Forward;
		forwardFlat = forwardFlat.Normalized();
		Vector3 target = camGlobal.Origin + forwardFlat * distance;
		target.Y = camGlobal.Origin.Y - downOffset;

		float t = (float)(delta * FollowSpeed);
		GlobalPosition = GlobalPosition.Lerp(target, t);

		Vector3 lookAt = new(camGlobal.Origin.X, GlobalPosition.Y, camGlobal.Origin.Z);
		if (GlobalPosition.DistanceSquaredTo(lookAt) > 0.01f)
		{
			LookAt(lookAt, Vector3.Up, true);
		}
	}

	public override void _ExitTree()
	{
		VRPointerRegistry.Unregister(this);
		if (Instance == this) Instance = null;
	}
}
