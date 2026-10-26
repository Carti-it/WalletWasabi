using System.Text;
using System.Text.Json.Nodes;
using System.Linq;
using WalletWasabi.Logging;
using System.Diagnostics.CodeAnalysis;

namespace WalletWasabi.Coordinator.Configuration;

public class JsonDiffHelper
{
	/// <summary>
	/// Computes a diff string containing lines in the following format:
	/// 
	/// + [addedProperty]: [newValue]
	/// - [removedProperty]: [oldValue]
	/// ~ [modifiedProperty]: [oldValue] -> [newValue]
	/// </summary>
	public static bool ComputeDiff(
		string oldJson,
		string newJson,
		[NotNullWhen(true)] out string? characterChange,
		[NotNullWhen(true)] out string? diff)
	{
		try
		{
			var left = JsonNode.Parse(oldJson);
			var right = JsonNode.Parse(newJson);
			var sb = new StringBuilder();
			ComputeDiffCore(sb, path: "", left, right);

			int v = newJson.Length - oldJson.Length;
			characterChange = v switch
			{
				-1 => $"{v} character",
				1 => $"+{v} character",
				> 0 => $"+{v} characters",
				_ => $"{v} characters",
			};

			diff = sb.Length == 0 ? "No differences" : sb.ToString();

			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);

			characterChange = null;
			diff = null;
			return false;
		}
	}

	private static void ComputeDiffCore(StringBuilder sb, string path, JsonNode? left, JsonNode? right)
	{
		if (JsonNode.DeepEquals(left, right))
		{
			return;
		}

		if (left is JsonObject leftObject && right is JsonObject rightObject)
		{
			var keys = leftObject
				.Select(p => p.Key)
				.Union(rightObject.Select(p => p.Key));

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
					ComputeDiffCore(sb, newPath, l, r);
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
