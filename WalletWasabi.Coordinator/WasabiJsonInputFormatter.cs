using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;

namespace WalletWasabi.Coordinator;

public delegate Task<Result<object, string>> JsonDecoder(Stream stream, Type modelType);

public class WasabiJsonInputFormatter : TextInputFormatter
{
	private readonly JsonDecoder _decoder;
	private readonly bool _logRequestJson;

	public WasabiJsonInputFormatter(JsonDecoder decoder, bool? logRequestJson = false)
	{
		_decoder = decoder;
		_logRequestJson = logRequestJson ?? Logger.MinimumLogLevel <= LogLevel.Debug;

		SupportedEncodings.Add(Encoding.UTF8);
		SupportedMediaTypes.Add(MediaTypeNames.Application.Json);
	}

	public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
	{
		var httpContext = context.HttpContext;
		var request = httpContext.Request;

		// Enable buffering to allow reading the request body multiple times for logging purposes.
		if (_logRequestJson)
		{
			request.EnableBuffering();
		}

		var requestStream = httpContext.Request.Body;

		try
		{
			var modelDeserializationResult = await _decoder(requestStream, context.ModelType).ConfigureAwait(false);
			if (!modelDeserializationResult.IsOk)
			{
				Logger.LogError(modelDeserializationResult.Error);

				if (_logRequestJson)
				{
					LogRequestJson(requestStream, encoding);
				}

				return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
			}

			var model = modelDeserializationResult.Value;
			return InputFormatterResult.Success(model);
		}
		catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
		{
		}
		catch (Exception e)
		{
			Logger.LogError(e);

			if (_logRequestJson)
			{
				LogRequestJson(requestStream, encoding);
			}
		}

		return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
	}

	private static void LogRequestJson(Stream requestStream, Encoding encoding)
	{		
		try
		{
			// Requires the stream to support seeking. If the stream is not seekable, this will throw an exception.
			requestStream.Position = 0;

			using var streamReader = new StreamReader(requestStream, encoding);
			var request = streamReader.ReadToEnd();
			Logger.LogInfo($"Failed request's JSON: {request}");
		}
		catch (Exception e)
		{
			Logger.LogError("Failed to log the request's JSON.", e);
		}
	}
}
