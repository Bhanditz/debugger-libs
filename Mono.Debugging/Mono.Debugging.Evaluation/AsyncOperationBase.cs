using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
	public abstract class AsyncOperationBase
	{
		public Task Task { get; protected set; }

		public abstract string Description { get; }

		public Task InvokeAsync (CancellationToken token)
		{
			if (Task != null) throw new Exception("Task must be null");

			token.Register (() => {
				try {
					CancelImpl ();
				}
				catch (Exception e) {
					DebuggerLoggingService.LogMessage (e.Message);
				}
			});
			Task = InvokeAsyncImpl (token);
			return Task;
		}
		protected abstract Task InvokeAsyncImpl (CancellationToken token);

		protected abstract void CancelImpl ();

	}
}