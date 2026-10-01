using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.Coordinator;
using WalletWasabi.Serialization;
using WalletWasabi.WabiSabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Coordinator;

public class WasabiJsonInputFormatterTests
{
	[Fact]
	public async Task TruncatedJsonReturnsFailureAsync()
	{
		// Valid JSON for a round state request.
		string roundStateRequest = """
			{"RoundCheckpoints":[{"RoundId":"e207d7ed014c274df0ce30f30ffe4aed09a4d53d1be92140cdfb2627eeb73eb1","StateId":1}]}
			""";

		// Remove the last character to truncate the JSON to make it invalid.
		string truncatedJson = roundStateRequest[..^1];

		var formatter = new WasabiJsonInputFormatter(Decode.CoordinatorMessageFromStreamAsync, logRequestJson: true);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(truncatedJson));
		httpContext.Request.ContentType = MediaTypeNames.Application.Json;

		var context = new InputFormatterContext(
			httpContext,
			modelName: "model",
			modelState: new ModelStateDictionary(),
			metadata: new EmptyModelMetadataProvider().GetMetadataForType(typeof(RoundStateRequest)),
			readerFactory: (stream, encoding) => new StreamReader(stream, encoding));

		// Test that the formatter returns a failure result when reading the truncated JSON.
		var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

		Assert.True(result.HasError);
		Assert.Null(result.Model);
	}
}
