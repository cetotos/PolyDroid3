// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.WebAPI;
using Polytoria.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Polytoria.Providers.Datastore;

public class PTDatastoreProvider : IDatastoreProvider
{
	private const int MaxReadRequestsPerMinute = 30;
	private const int ReadRequestsPerPlayerModifier = 10;
	private const int MaxWriteRequestsPerMinute = 30;
	private const int WriteRequestsPerPlayerModifier = 10;

	private string _dsKey = "";
	private readonly PTHttpClient _client = new();
	private readonly Dictionary<string, DatastoreEntry> _data = [];
	private static int _readRequestsThisMinute = 0, _writeRequestThisMinute = 0, _currentMinute = 0;
	private Datamodel.Data.Datastore _ds = null!;

	public void Connect(string key, Datamodel.Data.Datastore ds)
	{
		_dsKey = key;
		_ds = ds;
		_client.DefaultRequestHeaders["Authorization"] = PolyServerAPI.AuthToken;
	}

	public bool UseReadRequest()
	{
		if (_currentMinute != DateTime.Now.Minute)
		{
			_currentMinute = DateTime.Now.Minute;
			_readRequestsThisMinute = 0;
		}

		if (_readRequestsThisMinute >= MaxReadRequestsPerMinute + (ReadRequestsPerPlayerModifier * _ds.DatastoreService.Root.Players.PlayersCount))
		{
			return false;
		}
		else
		{
			_readRequestsThisMinute++;
			return true;
		}
	}

	public bool UseWriteRequest()
	{
		if (_currentMinute != DateTime.Now.Minute)
		{
			_currentMinute = DateTime.Now.Minute;
			_writeRequestThisMinute = 0;
		}

		if (_writeRequestThisMinute >= MaxWriteRequestsPerMinute + (WriteRequestsPerPlayerModifier * _ds.DatastoreService.Root.Players.PlayersCount))
		{
			return false;
		}
		else
		{
			_writeRequestThisMinute++;
			return true;
		}
	}


	public async Task<object?> ReadData(string key)
	{
		if (!UseReadRequest()) throw new PTDatastoreQuotaException("Read quota exceeded");
		await LoadDatastore();
		if (_data.TryGetValue(key, out DatastoreEntry entry))
		{
			return entry.Value;
		}
		else
		{
			return null;
		}
	}

	public async Task WriteData(string key, object? value)
	{
		if (value != null && !CheckSupportedType(value))
		{
			throw new InvalidOperationException("Invalid value type");
		}
		if (!UseWriteRequest()) throw new PTDatastoreQuotaException("Write quota exceeded");

		JsonObject json = [];
		JsonNode? valueNode = null;

		if (value == null)
		{
			valueNode = null;
		}
		else if (value is string stringValue)
		{
			valueNode = JsonValue.Create(stringValue);
		}
		else if (value is bool boolValue)
		{
			valueNode = JsonValue.Create(boolValue);
		}
		else if (value is double doubleValue)
		{
			valueNode = JsonValue.Create(doubleValue);
		}

		json.Add(key, valueNode);

		List<KeyValuePair<string, string>> formVariables =
		[
			new("key", _dsKey),
			new("data", json.ToJsonString()),
		];
		FormUrlEncodedContent formContent = new(formVariables);

		using var req = await _client.PostAsync(Globals.ApiEndpoint.PathJoin("/v1/game/server/datastore/set-data"), formContent);
	}

	private async Task LoadDatastore()
	{
		using var req = await _client.GetAsync(Globals.ApiEndpoint.PathJoin("/v1/game/server/datastore/get-data?key=" + Uri.EscapeDataString(_dsKey)));
		LoadDatastoreJSON(await req.Content.ReadAsStringAsync());
	}

	private void LoadDatastoreJSON(string jsonData)
	{
		using JsonDocument document = JsonDocument.Parse(jsonData);
		JsonElement root = document.RootElement;

		foreach (JsonProperty property in root.EnumerateObject())
		{
			DatastoreEntry dsEntry = new() { Timestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds() };
			JsonElement val = property.Value;

			switch (val.ValueKind)
			{
				case JsonValueKind.True:
				case JsonValueKind.False:
					dsEntry.Value = val.GetBoolean();
					break;
				case JsonValueKind.String:
					dsEntry.Value = val.GetString() ?? "";
					break;
				case JsonValueKind.Number:
					dsEntry.Value = val.GetDouble();
					break;
			}

			_data[property.Name] = dsEntry;
		}
	}

	private static bool CheckSupportedType(object obj)
	{
		if (obj == null) return false;
		if (obj is int) return true;
		if (obj is double) return true;
		if (obj is float) return true;
		if (obj is string) return true;
		if (obj is bool) return true;
		return false;
	}


	public void Dispose()
	{
		_data.Clear();
		GC.SuppressFinalize(this);
	}

	private struct DatastoreEntry
	{
		public object Value;
		public float Timestamp;
	}

	public class PTDatastoreQuotaException(string msg) : Exception(msg);
}
