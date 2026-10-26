using System.IO;
using System.Text;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Coordinator;

namespace WalletWasabi.Coordinator.Configuration;

/// <summary>
/// Manager that provides up to date <see cref="WabiSabiConfig"/> and stores the config to disk.
/// </summary>
/// <remarks>Note that the manager is not thread safe.</remarks>
public class WabiSabiConfigManager : IDisposable
{
	public WabiSabiConfigManager(string path)
	{
		_path = path;
		_config = WabiSabiConfig.TryLoadFile(path, out _sourceJson) ??
			throw new InvalidOperationException($"Fix config '{path}' file.");
	}

	/// <remarks>Constructor for tests which does not lead to config reloading.</remarks>
	public WabiSabiConfigManager(WabiSabiConfig config)
	{
		_path = null;
		_config = config;
	}

	/// <summary>Path with WabiSabi config, or null if the config is provided directly.</summary>
	private readonly string? _path;

	private WabiSabiConfig _config;

	private string? _sourceJson;

	public WabiSabiConfig GetCurrent()
	{
		if (_path is null)
		{
			return _config;
		}
		else
		{
			var newConfig = WabiSabiConfig.TryLoadFile(_path, out var newSourceJson);
			if (newConfig is null)
			{
				// Return existing config instead.
				return _config;
			}
			else
			{
				string? oldSourceJson;

				var oldConfig = _config;
				_config = newConfig;
				oldSourceJson = _sourceJson;
				_sourceJson = newSourceJson;

				// This property can change while the JSON config file changes on disk. Resolve the conflict.
				if (oldConfig.CoordinatorExtPubKeyCurrentDepth != newConfig.CoordinatorExtPubKeyCurrentDepth)
				{
					var oldValue = oldConfig.CoordinatorExtPubKeyCurrentDepth;
					var newValue = newConfig.CoordinatorExtPubKeyCurrentDepth;
					Logger.LogInfo($"Conflict detected: Coordinator's ExtPubKey depths differ between configs ({oldValue} and {newValue}). Picking the larger value from the two.");

					var updatedConfig = newConfig with
					{
						CoordinatorExtPubKeyCurrentDepth = Math.Max(oldValue, newValue),
					};

					_config = updatedConfig;
					_sourceJson = SaveConfig(_path);
				}

				// JSON strings of the old and new config differs.
				if (oldSourceJson is not null && newSourceJson is not null && oldSourceJson != newSourceJson)
				{
					var success = JsonDiffHelper.ComputeDiff(oldSourceJson, newSourceJson, out var characterChange, out var diff);

					var msg = success
						? $"WabiSabi config file '{_path}' was updated ({characterChange}):\n{diff}"
						: $"WabiSabi config file '{_path}' was updated. Diff failed to be computed.";
					Logger.LogInfo(msg);
				}

				return newConfig;
			}
		}
	}

	public string SaveConfig(string path)
	{
		var json = _config.EncodeAsJson();
		File.WriteAllText(path, json, Encoding.UTF8);
		return json;
	}

	public void Dispose()
	{
		if (_path is not null)
		{
			Logger.LogInfo($"Storing the latest config '{_path}'.");
			SaveConfig(_path);
		}
	}
}
