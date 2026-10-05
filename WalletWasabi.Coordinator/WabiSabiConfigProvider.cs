using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Coordinator;

namespace WalletWasabi.Coordinator;

public class WabiSabiConfigProvider
{
	public WabiSabiConfigProvider(string path)
	{
		_path = path;
		_config = WabiSabiConfig.TryLoadFile(path, out _sourceJson) ??
			throw new InvalidOperationException($"Fix config '{path}' file.");
	}

	/// <remarks>Constructor for tests which does not lead to config reloading.</remarks>
	public WabiSabiConfigProvider(WabiSabiConfig config)
	{
		_path = null;
		_config = config;
	}

	/// <summary>Path with WabiSabi config, or null if the config is provided directly.</summary>
	private readonly string? _path;
	private readonly Lock _lock = new();

	/// <remarks>Access requires <see cref="_lock"/>.</remarks>
	private WabiSabiConfig _config;

	/// <remarks>Access requires <see cref="_lock"/>.</remarks>
	private string? _sourceJson;

	public WabiSabiConfig GetCurrent()
	{
		if (_path is null)
		{
			lock (_lock)
			{
				return _config;
			}
		}
		else
		{
			var newConfig = WabiSabiConfig.TryLoadFile(_config.FilePath, out var newSourceJson);
			if (newConfig is null)
			{
				// Return existing config instead.
				lock (_lock)
				{
					return _config;
				}
			}
			else
			{
				string? oldSourceJson;

				lock (_lock)
				{
					_config = newConfig;
					oldSourceJson = _sourceJson;
					_sourceJson = newSourceJson;
				}

				if (oldSourceJson is not null && newSourceJson is not null && oldSourceJson != newSourceJson)
				{
					int v = newSourceJson.Length - oldSourceJson.Length;
					string characterChange = v switch
					{
						-1 => $"{v} character",
						1 => $"+{v} character",
						> 0 => $"+{v} characters",
						_ => $"{v} characters",
					};

					Logger.LogInfo($"WabiSabi config file '{_config.FilePath}' was updated ({characterChange}):\n{GetNiceJsonDiff(oldSourceJson, newSourceJson)}");
				}

				return newConfig;
			}
		}
	}

	private static string? GetNiceJsonDiff(string oldJson, string newJson)
	{
		try
		{
			var left = JsonNode.Parse(oldJson);
			var right = JsonNode.Parse(newJson);
			var sb = new StringBuilder();
			Diff(sb, path: "", left, right);
			return sb.Length == 0 ? "No differences" : sb.ToString();
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			return null;
		}
	}

	private static void Diff(StringBuilder sb, string path, JsonNode? left, JsonNode? right)
	{
		if (JsonNode.DeepEquals(left, right))
		{
			return;
		}

		if (left is JsonObject leftObject && right is JsonObject rightObject)
		{
			var keys = leftObject.Select(p => p.Key).Union(rightObject.Select(p => p.Key));

			foreach (var key in keys)
			{
				var l = leftObject[key];
				var r = rightObject[key];
				var newPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";

				if (l is null)
				{
					sb.AppendLine($"+ {newPath}: {r}");
				}
				else if (r is null)
				{
					sb.AppendLine($"- {newPath}: {l}");
				}
				else
				{
					Diff(sb, newPath, l, r);
				}
			}
		}
		else if (left is JsonArray || right is JsonArray)
		{
			sb.AppendLine($"~ {path}: array changed");
		}
		else
		{
			sb.AppendLine($"~ {path}: {left} -> {right}");
		}
	}
}
