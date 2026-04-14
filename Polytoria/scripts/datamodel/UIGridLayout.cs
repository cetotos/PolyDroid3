// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class UIGridLayout : UIContainer
{
	private int _columns;
	private int _spacing;

	[Editable, ScriptProperty, DefaultValue(8)]
	public int Spacing
	{
		get => _spacing;
		set
		{
			_spacing = value;
			NodeControl.AddThemeConstantOverride("h_separation", _spacing);
			NodeControl.AddThemeConstantOverride("v_separation", _spacing);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1)]
	public int Columns
	{
		get => _columns;
		set
		{
			_columns = value;
			((GridContainer)NodeControl).Columns = _columns;
			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode()
	{
		return new GridContainer();
	}

	public enum UILayoutAlignmentEnum
	{
		Start,
		Center,
		End,
	}
}
