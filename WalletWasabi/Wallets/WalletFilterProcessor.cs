using Microsoft.Extensions.Hosting;
using Nito.AsyncEx;
using WalletWasabi.Backend.Models;
using WalletWasabi.BitcoinRpc;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Stores;
using WalletWasabi.Wallets.FilterProcessor;

namespace WalletWasabi.Wallets;

public class WalletFilterProcessor : BackgroundService
{
	public WalletFilterProcessor(
		KeyManager keyManager,
		AllTransactionStore transactionStore,
		FilterStore filterStore,
		FilterHeaderChain filterHeaderChain,
		TransactionProcessor transactionProcessor,
		BlockProvider blockProvider,
		IRPCClient? bitcoinRpcClient,
		EventBus eventBus)
	{
		_keyManager = keyManager;
		_transactionStore = transactionStore;
		_filterHeaderChain = filterHeaderChain;
		_transactionProcessor = transactionProcessor;
		_blockProvider = blockProvider;
		_bitcoinRpcClient = bitcoinRpcClient;
		_eventBus = eventBus;
		_blockFilterIterator = new(filterStore);
		_initialSynchronizationFinished = new TaskCompletionSource();
	}

	private readonly KeyManager _keyManager;
	private readonly AllTransactionStore _transactionStore;
	private readonly FilterHeaderChain _filterHeaderChain;
	private readonly TransactionProcessor _transactionProcessor;
	private readonly BlockProvider _blockProvider;
	private readonly IRPCClient? _bitcoinRpcClient;
	private readonly EventBus _eventBus;
	private readonly BlockFilterIterator _blockFilterIterator;
	private readonly TaskCompletionSource _initialSynchronizationFinished;

	public Task InitialSynchronizationFinished => _initialSynchronizationFinished.Task;

	/// <summary>Make sure we don't process any request while a reorg is happening.</summary>
	private readonly AsyncLock _reorgLock = new();

	private IDisposable? _chainReorgSubscription;

	/// <inheritdoc />
	/// <summary>Used for filter synchronization.</summary>
	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.WaitForAsync(() => _filterHeaderChain is {HashCount: > 0, HashesLeft: < 100}, cancellationToken).ConfigureAwait(false);

			while (!cancellationToken.IsCancellationRequested)
			{
				using (await _reorgLock.LockAsync(cancellationToken).ConfigureAwait(false))
				{
					var lastHeight = _keyManager.GetBestHeight();

					if (lastHeight == _filterHeaderChain.TipHeight)
					{
						_initialSynchronizationFinished.TrySetResult();
						await Task.Delay(1_000, cancellationToken).ConfigureAwait(false);
						continue;
					}

					var currentHeight = lastHeight + 1;
					var filter = await _blockFilterIterator.GetAndRemoveAsync(currentHeight, cancellationToken).ConfigureAwait(false);
					if (filter is null)
					{
						// The wallet being processed had been synchronized until a blockchain height which is higher
						// than the top filters that Wasabi has received. That means that the filters were reset, or
						// the wallet was copied and pasted from a more updated setup.
						// Wait for the index store to catch up.
						await Task.Delay(2_000, cancellationToken).ConfigureAwait(false);
						continue;
					}
					var matchFound = await ProcessFilterModelAsync(filter, cancellationToken).ConfigureAwait(false);
					_eventBus.Publish(new FilterProcessed(filter));

					var reachedBlockChainTip = currentHeight == _filterHeaderChain.TipHeight;
					bool storeToDisk = matchFound || reachedBlockChainTip;
					_keyManager.SetBestHeight(currentHeight, storeToDisk);
				}
			}
		}
		catch (OperationCanceledException)
		{
			Logger.LogDebug("Filter processor's execution was stopped.");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex);
			TerminateService.Instance?.SignalGracefulCrash(ex);
			throw;
		}
	}

	/// <param name="tweakData">Tweak data for each eligible transaction of the block to process.</param>
	private IEnumerable<byte[]> GetSilentPaymentScriptPubKeysToTest(byte[][] tweakData) =>
		tweakData.SelectMany(_keyManager.GetSilentPaymentSynchronizationScripts);

	private async Task<bool> ProcessFilterModelAsync(FilterModel filter, CancellationToken cancellationToken)
	{
		var blockHash = filter.Header.BlockHash;
		var toTestKeys = _keyManager.UnsafeGetSynchronizationInfos();

		if (_keyManager.GetIsSilentPaymentReceivingEnabled() && _bitcoinRpcClient is not null)
		{
			var verboseBlockInfo = await _bitcoinRpcClient.GetVerboseBlockAsync(blockHash, cancellationToken).ConfigureAwait(false);

			var tweakDataForTransactions = SilentPayment.SilentPayment.BuildSilentPaymentTweakData(verboseBlockInfo);

			var scanData = _keyManager.GetSilentPaymentScanData();
			var silentPaymentAddresses = scanData.Select(x => x.Address).ToArray();

			/*
			foreach (var spendingTx in verboseBlockInfo.Transactions)
			{
				if (SilentPayment.SilentPayment.IsEligible(spendingTx))
				{
					var prevOuts = spendingTx.Inputs.Select(x => x.PrevOut).ToArray();
					var pubKeys = spendingTx.Inputs
						.Select(x => SilentPayment.SilentPayment.ExtractPubKey(x.ScriptSig, x.WitScript, null))
						.DropNulls()
						.ToArray();

					var tweakData = SilentPayment.SilentPayment.TweakData(prevOuts, pubKeys);

					foreach (var scanDataItem in scanData)
					{
						var dictionary = SilentPayment.SilentPayment.ExtractSilentPaymentScriptPubKeys([scanDataItem.Address], tweakData, spendingTx, scanDataItem.ScanSecret);

						if (dictionary.Count > 0)
						{
							return true;
						}
					}
				}
			}
			*/
		}


		var matchFound = false;
		if (toTestKeys.Length != 0)
		{
			matchFound = filter.Filter.MatchAny(toTestKeys, filter.FilterKey);

			if (matchFound)
			{
				// Wait until downloaded.
				var block = await GetBlockAsync(blockHash, cancellationToken).ConfigureAwait(false);

				var blockHeight = filter.Header.Height;
				_eventBus.Publish(new BlockDownloaded(blockHeight));

				var height = new ChainHeight(blockHeight);
				var blockTime = block.Header.BlockTime;
				var blockTransactions = block.Transactions;
				var txsToProcess = new List<SmartTransaction>(capacity: blockTransactions.Count);

				for (int i = 0; i < blockTransactions.Count; i++)
				{
					var tx = new SmartTransaction(blockTransactions[i], height, blockHash, blockIndex: i, firstSeen: blockTime);
					txsToProcess.Add(tx);
				}

				_transactionProcessor.Process(txsToProcess);
			}
		}

		return matchFound;
	}

	private async Task<Block> GetBlockAsync(uint256 blockHash, CancellationToken cancellationToken)
	{
		Logger.LogInfo($"Obtaining block {blockHash}...");
		var block = await _blockProvider(blockHash, cancellationToken).ConfigureAwait(false);

		return block is not null
			? block
			: throw new InvalidOperationException($"Block {blockHash} was not found.");
	}

	private async void ReorgedAsync(uint256 invalidBlockHash, ChainHeight invalidBlockHeight)
	{
		try
		{
			var newBestHeight = invalidBlockHeight - 1;

			using (await _reorgLock.LockAsync(CancellationToken.None).ConfigureAwait(false))
			{
				_keyManager.SetMaxBestHeight(newBestHeight);
				_transactionProcessor.UndoBlock(invalidBlockHeight);
				_transactionStore.ReleaseToMempoolFromBlock(invalidBlockHash);
				_blockFilterIterator.RemoveNewerThan(newBestHeight);
			}
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex);
		}
	}

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		_chainReorgSubscription = _eventBus.Subscribe<ChainReorganized>(e => ReorgedAsync(e.invalidBlockHash, e.invalidBlockHeight));
		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_chainReorgSubscription?.Dispose();
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}
}

public static class TaskExtensions
{
	extension(Task)
	{
		public static async Task WaitForAsync(Func<bool> predicate, CancellationToken cancellationToken)
		{
			while (!predicate())
			{
				await Task.Delay(1_000, cancellationToken).ConfigureAwait(false);
			}
		}
	}
}
