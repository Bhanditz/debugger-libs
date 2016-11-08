using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
	public class AsyncOperationManager1
	{
		readonly HashSet<AsyncOperationBase> tasks = new HashSet<AsyncOperationBase> ();
		bool disposed = false;
		private CancellationTokenSource global = new CancellationTokenSource ();


		public void Invoke (AsyncOperationBase mc, int timeout)
		{
			if (timeout <= 0)
				throw new ArgumentOutOfRangeException("timeout", timeout, "timeout must be greater than 0");

			Task task;
			CancellationTokenSource cts;
			lock (tasks) {
				if (disposed)
					throw new ObjectDisposedException ("Already disposed");
				cts = CancellationTokenSource.CreateLinkedTokenSource (global.Token);
				task = mc.InvokeAsync (cts.Token);
				tasks.Add (mc);
			}

			task.ContinueWith (tsk => {
				lock (tasks) {
					tasks.Remove (mc);
				}
			}, cts.Token);

			try {
				if (!task.Wait (timeout)) {
					cts.Cancel ();
					WaitAfterCancel (task, mc.Description);
					throw new TimeOutException ();
				}
			}
			catch (OperationCanceledException e) {
				throw new EvaluatorAbortedException ();
			}
			catch (AggregateException e) {
				if (e.InnerExceptions.OfType<OperationCanceledException> ().Any ()) {
					throw new EvaluatorAbortedException ();
				}
			}
		}


		public event EventHandler<BusyStateEventArgs> BusyStateChanged = delegate {  };

		private void WaitAfterCancel (Task op, string desc)
		{
			if (!op.Wait (500)) {
				try {
					BusyStateChanged (this, new BusyStateEventArgs {IsBusy = true, Description = desc});
					op.Wait (2000);
				}
				finally {
					BusyStateChanged (this, new BusyStateEventArgs {IsBusy = false, Description = desc});
				}
			}
		}


		public void AbortAll ()
		{
			List<AsyncOperationBase> copy;
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
				WaitAfterCancel (task.Task, task.Description);
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