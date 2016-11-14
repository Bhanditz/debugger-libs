using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
	public class AsyncOperationManager
	{
		readonly HashSet<IAsyncOperationBase> tasks = new HashSet<IAsyncOperationBase> ();
		bool disposed = false;
		CancellationTokenSource global = new CancellationTokenSource ();
		const int ShortCancelTimeout = 100;
		const int LongCancelTimeout = 100;

		static bool IsOperationCancelledException (Exception e)
		{
			if (e is OperationCanceledException)
				return true;
			var aggregateException = e as AggregateException;

			if (aggregateException != null && aggregateException.InnerExceptions.OfType<OperationCanceledException> ().Any ())
				return true;
			return false;
		}

		public OperationResult<TValue> Invoke<TValue> (AsyncOperationBase<TValue> mc, int timeout)
		{
			if (timeout <= 0)
				throw new ArgumentOutOfRangeException("timeout", timeout, "timeout must be greater than 0");

			Task<OperationResult<TValue>> task;
			CancellationTokenSource cts;
			var description = mc.Description;
			lock (tasks) {
				if (disposed)
					throw new ObjectDisposedException ("Already disposed");
				cts = CancellationTokenSource.CreateLinkedTokenSource (global.Token);
				DebuggerLoggingService.LogMessage (string.Format("Starting invoke for {0}", description));
				task = mc.InvokeAsync (cts.Token);
				tasks.Add (mc);
			}

			task.ContinueWith (tsk => {
				lock (tasks) {
					tasks.Remove (mc);
				}
			}, cts.Token);

			bool cancelledAfterTimeout = false;
			try {
				if (task.Wait (timeout)) {
					DebuggerLoggingService.LogMessage (string.Format("Invoke {0} succeeded in {1} ms", description, timeout));
					return task.Result;
				}
				DebuggerLoggingService.LogMessage (string.Format("Invoke {0} timed out after {1} ms. Cancelling.", description, timeout));
				cts.Cancel ();
				try {
					WaitAfterCancel (mc);
				}
				catch (Exception e) {
					if (IsOperationCancelledException (e)) {
						DebuggerLoggingService.LogMessage (string.Format ("Invoke {0} was cancelled after timeout", description));
						cancelledAfterTimeout = true;
					}
					throw;
				}
				DebuggerLoggingService.LogMessage (string.Format("{0} cancelling timed out", description));
				throw new TimeOutException ();
			}
			catch (Exception e) {
				if (IsOperationCancelledException (e)) {
					if (cancelledAfterTimeout)
						throw new TimeOutException ();
					DebuggerLoggingService.LogMessage (string.Format("Invoke {0} was cancelled outside before timeout", description));
					throw new EvaluatorAbortedException ();
				}
				throw;
			}
		}


		public event EventHandler<BusyStateEventArgs> BusyStateChanged = delegate {  };

		void WaitAfterCancel (IAsyncOperationBase op)
		{
			var desc = op.Description;
			DebuggerLoggingService.LogMessage (string.Format ("Waiting for cancel of invoke {0}", desc));
			try {
				if (!op.RawTask.Wait (ShortCancelTimeout)) {
					try {
						BusyStateChanged (this, new BusyStateEventArgs {IsBusy = true, Description = desc});
						op.RawTask.Wait (LongCancelTimeout);
					}
					finally {
						BusyStateChanged (this, new BusyStateEventArgs {IsBusy = false, Description = desc});
					}
				}
			}
			finally {
				DebuggerLoggingService.LogMessage (string.Format ("Calling AfterCancelled() for {0}", desc));
				op.AfterCancelled (ShortCancelTimeout + LongCancelTimeout);
			}
		}


		public void AbortAll ()
		{
			DebuggerLoggingService.LogMessage ("Aborting all the current invocations");
			List<IAsyncOperationBase> copy;
			CancellationTokenSource oldGlobal;
			lock (tasks) {
				if (disposed) throw new ObjectDisposedException ("Already disposed");
				copy = tasks.ToList ();
				oldGlobal = global;
				tasks.Clear ();
				global = new CancellationTokenSource ();
			}

			oldGlobal.Cancel();
			foreach (var task in copy) {
				var taskDescription = task.Description;
				try {
					WaitAfterCancel (task);
				}
				catch (Exception e) {
					if (IsOperationCancelledException (e)) {
						DebuggerLoggingService.LogMessage (string.Format ("Invocation of {0} cancelled in AbortAll()", taskDescription));
					}
					else {
						DebuggerLoggingService.LogError (string.Format ("Invocation of {0} thrown an exception in AbortAll()", taskDescription), e);
					}
				}
			}
		}


		public void Dispose ()
		{
			lock (tasks) {
				if (disposed) throw new ObjectDisposedException ("Already disposed");
				disposed = true;
			}

			global.Cancel ();
		}
	}
}