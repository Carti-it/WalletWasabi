using System.Collections.Generic;
using System.Threading.Tasks;

namespace WalletWasabi.Services;

public class ComposedDisposable : IDisposable
{
	private readonly List<IDisposable> _disposables = [];
	private bool _isDisposed = false;

	public ComposedDisposable Add(IDisposable disposable)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		_disposables.Add(disposable);
		return this;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_disposables.Reverse();
				foreach (var disposable in _disposables)
				{
					disposable.Dispose();
				}

				_disposables.Clear();
			}

			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}

/// <summary>
/// Class for disposing multiple <see cref="IDisposable"/> and <see cref="IAsyncDisposable"/> objects in a single call to <see cref="DisposeAsync"/>.
/// </summary>
public class ComposedAsyncDisposable : IAsyncDisposable
{
	private readonly List<object> _disposables = [];
	private bool _isDisposed = false;

	public ComposedAsyncDisposable Add(IDisposable disposable)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		_disposables.Add(disposable);
		return this;
	}

	public ComposedAsyncDisposable Add(IAsyncDisposable disposable)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		_disposables.Add(disposable);
		return this;
	}

	public async ValueTask DisposeAsync()
	{
		if (!_isDisposed)
		{
			_isDisposed = true;

			_disposables.Reverse();
			foreach (var disposable in _disposables)
			{
				if (disposable is IAsyncDisposable asyncDisposable)
				{
					await asyncDisposable.DisposeAsync().ConfigureAwait(false);
				}
				else if (disposable is IDisposable syncDisposable)
				{
					syncDisposable.Dispose();
				}
				else
				{
					throw new InvalidOperationException($"Unexpected disposable type: {disposable.GetType()}");
				}
			}
		}
	}
}

public static class DisposableExtensions
{
	public static ComposedDisposable DisposeUsing(this IDisposable disposable, ComposedDisposable container)
	{
		container.Add(disposable);
		return container;
	}

	public static ComposedAsyncDisposable DisposeUsing(this IAsyncDisposable disposable,
		ComposedAsyncDisposable container)
	{
		container.Add(disposable);
		return container;
	}

	public static ComposedAsyncDisposable DisposeUsing(this IDisposable disposable,
		ComposedAsyncDisposable container)
	{
		container.Add(disposable);
		return container;
	}
}
