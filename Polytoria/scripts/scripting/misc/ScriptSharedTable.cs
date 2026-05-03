// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using System.Collections.Generic;

namespace Polytoria.Scripting;

public partial class ScriptSharedTable : IScriptObject
{
	internal Dictionary<object, object> SharedDict = [];

	[ScriptMethod]
	public void Clear()
	{
		SharedDict.Clear();
	}

	[ScriptMethod]
	public void Remove(string key)
	{
		SharedDict.Remove(key);
	}

	[ScriptMethod]
	public void ClearPrefix(string prefix)
	{
		foreach ((object key, _) in SharedDict)
		{
			if (key is string strk && strk.StartsWith(prefix))
			{
				SharedDict.Remove(key);
			}
		}
	}

	[ScriptMethod]
	public void ClearSuffix(string suffix)
	{
		foreach ((object key, _) in SharedDict)
		{
			if (key is string strk && strk.EndsWith(suffix))
			{
				SharedDict.Remove(key);
			}
		}
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Index)]
	public object? Index(object index)
	{
		if (SharedDict.TryGetValue(index, out object? value))
		{
			return value;
		}
		return null;
	}

	[ScriptMetamethod(ScriptObjectMetamethod.NewIndex)]
	public void NewIndex(object index, object val)
	{
		SharedDict[index] = val;
		if (val == null)
		{
			SharedDict.Remove(index);
		}
	}

	[ScriptMetamethod(ScriptObjectMetamethod.Iter)]
	public static IEnumerable<(object, object)> Iter(ScriptSharedTable sTable)
	{
		foreach ((var key, var value) in sTable.SharedDict)
		{
			yield return (key, value);
		}
	}
}
