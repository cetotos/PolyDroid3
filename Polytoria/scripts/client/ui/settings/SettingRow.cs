using Godot;
using Polytoria.Shared;
using Polytoria.Shared.Settings;
using System.Linq;

namespace Polytoria.Client.UI;

public sealed partial class SettingRow : PanelContainer
{
	public SettingDef Definition = null!;
	public ISettingsContext Context = null!;

	private Label _title = null!;
	private Label? _desc;
	private Control _field = null!;
	private Label _disabledLabel = null!;

	public override void _Ready()
	{
		HBoxContainer root = new();
		AddChild(root);

		VBoxContainer textLayout = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		root.AddChild(textLayout);

		_title = new Label
		{
			Text = Definition.Label,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		_title.AddThemeFontSizeOverride("font_size", 24);
		textLayout.AddChild(_title);

		if (!string.IsNullOrEmpty(Definition.Description))
		{
			_desc = new Label
			{
				Text = Definition.Description,
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			};
			_desc.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
			textLayout.AddChild(_desc);
		}

		if (Definition.RequiresRestart)
		{
			Label restart = new()
			{
				Text = "Restart required to apply changes."
			};
			restart.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
			restart.AddThemeFontSizeOverride("font_size", 14);
			textLayout.AddChild(restart);
		}

		_disabledLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Visible = false
		};
		_disabledLabel.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.35f));
		_disabledLabel.AddThemeFontSizeOverride("font_size", 14);
		textLayout.AddChild(_disabledLabel);

		_field = SettingFieldFactory.Create(Definition);
		_field.CustomMinimumSize = new Vector2(220, 0);
		root.AddChild(_field);

		if (Definition.Conditions != null)
		{
			Visible = Definition.Conditions.Any((cond) =>
			{
				object? value = Context.GetUntyped(cond.Target);
				return cond.UntypedPredicate(value);
			});
		}

		RefreshDisabled();
		Callable.From(RefreshDisabled).CallDeferred();

		Context.Changed += OnExternalChanged;

		base._Ready();
	}

	private void RefreshDisabled()
	{
		string? reason = Definition.DisabledText?.Invoke(Context);
		if (reason == null
			&& Definition.Key == SharedSettingKeys.PostProcessing.RtReflections
			&& !RtReflectionsSupported())
		{
			reason = Globals.IsMobileBuild
				? "Ray tracing is not available on mobile devices."
				: "Unavailable: requires the Vulkan graphics API and the Standard renderer.";
		}

		bool disabled = reason != null;
		Color dim = disabled ? new Color(1f, 1f, 1f, 0.35f) : Colors.White;
		_title.Modulate = dim;
		if (_desc != null)
		{
			_desc.Modulate = dim;
		}
		_field.Modulate = dim;
		SetInteractable(_field, !disabled);
		_disabledLabel.Visible = disabled;
		if (disabled)
		{
			_disabledLabel.Text = reason;
		}
	}

	private static void SetInteractable(Control node, bool enabled)
	{
		node.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		switch (node)
		{
			case Slider slider:
				slider.Editable = enabled;
				break;
			case SpinBox spin:
				spin.Editable = enabled;
				break;
			case LineEdit line:
				line.Editable = enabled;
				break;
		}
		foreach (Node child in node.GetChildren())
		{
			if (child is Control control)
			{
				SetInteractable(control, enabled);
			}
		}
	}

	private static bool RtReflectionsSupported()
	{
		if (Globals.IsMobileBuild)
		{
			return false;
		}
		return RenderingServer.GetCurrentRenderingMethod() == "forward_plus"
			&& RenderingServer.GetCurrentRenderingDriverName().Equals("vulkan", System.StringComparison.OrdinalIgnoreCase);
	}

	private void OnExternalChanged(SettingChangedEvent e)
	{
		if (Definition.Conditions != null)
		{
			var match = Definition.Conditions.Where(c => c.Target == e.Key);
			if (match.Any())
			{
				Visible = match.Any(c => c.UntypedPredicate(e.NewValue));
			}
		}

		RefreshDisabled();
	}

	public override void _ExitTree()
	{
		Context?.Changed -= OnExternalChanged;
		base._ExitTree();
	}
}
