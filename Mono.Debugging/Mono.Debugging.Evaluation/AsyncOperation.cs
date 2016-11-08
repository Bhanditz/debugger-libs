using System;

namespace Mono.Debugging.Evaluation
{
	public enum AsyncOperationState
	{
		WaitForRun,
		Running,
		Aborting,
		Aborted
	}

	public abstract class AsyncOperation : IAsyncOperation
	{
		internal AsyncOperationManager Manager;

		AsyncOperationState state = AsyncOperationState.WaitForRun;
		object sync = new object ();


		internal void InternalShutdown ()
		{
			lock (this) {
				if (Aborted)
					return;
				try {
					Aborted = true;
					Shutdown ();
				} catch {
					// Ignore
				}
			}
		}
		
		/// <summary>
		/// Returns a short description of the operation, to be shown in the Debugger Busy Dialog
		/// when it blocks the execution of the debugger. 
		/// </summary>
		public abstract string Description { get; }

		internal void InternalInvoke ()
		{
			lock (sync) {
				if (state != AsyncOperationState.WaitForRun)
					throw new InvalidOperationException (string.Format("Invoke can be called in WaitForRun state, but current state is {0}", state));
				state = AsyncOperationState.Running;
			}
			BeginInvoke ();
		}
		
		/// <summary>
		/// Called to invoke the operation. The execution must be asynchronous (it must return immediatelly).
		/// </summary>
		public abstract void BeginInvoke ( );

		internal void InternalAbort ()
		{


			System.Threading.Monitor.Enter (this);
			if (Aborted) {
				System.Threading.Monitor.Exit (this);
				return;
			}

			if (Aborting) {
				// Somebody else is aborting this. Just wait for it to finish.
				System.Threading.Monitor.Exit (this);
				WaitForCompleted (System.Threading.Timeout.Infinite);
				return;
			}

			Aborting = true;

			int abortState = 0;
			int abortRetryWait = 100;
			bool abortRequested = false;

			do {
				if (abortState > 0)
					System.Threading.Monitor.Enter (this);

				try {
					if (!Aborted && !abortRequested) {
						// The Abort() call doesn't block. WaitForCompleted is used below to wait for the abort to succeed
						Abort ();
						abortRequested = true;
					}
					// Short wait for the Abort to finish. If this wait is not enough, it will wait again in the next loop
					if (WaitForCompleted (100)) {
						System.Threading.Monitor.Exit (this);
						break;
					}
				}
				catch (Exception) {
					// If abort fails, try again after a short wait
					if (abortState > 20) {
						// somewhen invoke callback is not called, which cause debugger hanging. Need to break the loop
						System.Threading.Monitor.Exit (this);
						break;
					}
				}
				abortState++;
				if (abortState == 6) {
					// Several abort calls have failed. Inform the user that the debugger is busy
					abortRetryWait = 500;
					try {
						Manager.EnterBusyState (this);
					}
					catch (Exception ex) {
						Console.WriteLine (ex);
					}
				}
				System.Threading.Monitor.Exit (this);
			} while (!Aborted && !WaitForCompleted (abortRetryWait) && !Manager.Disposing);

			if (Manager.Disposing) {
				InternalShutdown ();
			}
			else {
				lock (this) {
					Aborted = true;
					if (abortState >= 6)
						Manager.LeaveBusyState (this);
				}
			}
		}

		/// <summary>
		/// Called to abort the execution of the operation. It has to throw an exception
		/// if the operation can't be aborted. This operation must not block. The engine
		/// will wait for the operation to be aborted by calling WaitForCompleted.
		/// </summary>
		public abstract void Abort ();

		/// <summary>
		/// Waits until the operation has been completed or aborted.
		/// </summary>
		public abstract bool WaitForCompleted (int timeout);
		
		/// <summary>
		/// Called when the debugging session has been disposed.
		/// I must cause any call to WaitForCompleted to exit, even if the operation
		/// has not been completed or can't be aborted.
		/// </summary>
		public abstract void Shutdown ();
	}
}