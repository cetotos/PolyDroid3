// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using System;
using System.Collections.Generic;

namespace Polytoria.Client;

public static class RecentPlacesStore
{
	private const string PathConst = "user://recent_places.cfg";
	private const string Section = "entries";
	private const int MaxEntries = 8;

	public static void Add(int id, string name, string creatorName, int playing, double? rating)
	{
		if (id <= 0) return;
		List<Godot.Collections.Dictionary> list = LoadRaw();
		list.RemoveAll(e => e["id"].AsInt32() == id);
		Godot.Collections.Dictionary entry = new()
		{
			["id"] = id,
			["name"] = name ?? "",
			["creator"] = creatorName ?? "",
			["playing"] = playing,
			["rating"] = rating ?? -1.0,
			["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
		};
		list.Insert(0, entry);
		while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
		SaveRaw(list);
	}

	public static List<APIWorldsData> GetRecent()
	{
		List<APIWorldsData> result = [];
		foreach (Godot.Collections.Dictionary e in LoadRaw())
		{
			double r = e["rating"].AsDouble();
			result.Add(new APIWorldsData
			{
				Id = e["id"].AsInt32(),
				Name = e["name"].AsString(),
				CreatorName = e["creator"].AsString(),
				Playing = e["playing"].AsInt32(),
				Rating = r < 0 ? null : r,
			});
		}
		return result;
	}

	private static List<Godot.Collections.Dictionary> LoadRaw()
	{
		List<Godot.Collections.Dictionary> list = [];
		ConfigFile cfg = new();
		if (cfg.Load(PathConst) != Error.Ok) return list;
		Variant v = cfg.GetValue(Section, "list", new Godot.Collections.Array());
		Godot.Collections.Array arr = v.AsGodotArray();
		foreach (Variant item in arr)
		{
			Godot.Collections.Dictionary d = item.AsGodotDictionary();
			if (d.ContainsKey("id")) list.Add(d);
		}
		return list;
	}

	private static void SaveRaw(List<Godot.Collections.Dictionary> list)
	{
		ConfigFile cfg = new();
		cfg.Load(PathConst);
		Godot.Collections.Array arr = [];
		foreach (Godot.Collections.Dictionary d in list) arr.Add(d);
		cfg.SetValue(Section, "list", arr);
		cfg.Save(PathConst);
	}
}
