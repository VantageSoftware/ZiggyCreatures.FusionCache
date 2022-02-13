﻿using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace ZiggyCreatures.Caching.Fusion.Internals.Backplane
{
	internal partial class BackplaneAccessor
	{
		private void ExecuteOperation(string operationId, string key, Action<CancellationToken> action, string actionDescription, FusionCacheEntryOptions options, CancellationToken token)
		{
			if (IsCurrentlyUsable(operationId, key) == false)
				return;

			token.ThrowIfCancellationRequested();

			var actionDescriptionInner = actionDescription + (options.AllowBackgroundBackplaneOperations ? " (background)" : null);

			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.LogDebug("FUSION (O={CacheOperationId} K={CacheKey}): " + actionDescriptionInner, operationId, key);

			FusionCacheExecutionUtils
				.RunSyncActionAdvanced(
					action,
					Timeout.InfiniteTimeSpan,
					false,
					options.AllowBackgroundBackplaneOperations == false,
					exc => ProcessError(operationId, key, exc, actionDescriptionInner),
					false,
					token
				)
			;
		}

		public bool Publish(string operationId, BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token)
		{
			if (IsCurrentlyUsable(operationId, message.CacheKey) == false)
				return false;

			// IGNORE MESSAGES -NOT- FROM THIS SOURCE
			if (message.SourceId != _cache.InstanceId)
			{
				if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
					_logger.Log(LogLevel.Warning, "FUSION (O={CacheOperationId} K={CacheKey}): cannot send a backplane message with a SourceId different than the local one (IFusionCache.InstanceId)", operationId, message.CacheKey);

				return false;
			}

			token.ThrowIfCancellationRequested();

			ExecuteOperation(
				operationId,
				message.CacheKey!,
				_ =>
				{
					_backplane.Publish(message, options);

					// EVENT
					_events.OnMessagePublished(operationId, message);
				},
				"sending backplane notification",
				options,
				token
			);

			return true;
		}
	}
}
