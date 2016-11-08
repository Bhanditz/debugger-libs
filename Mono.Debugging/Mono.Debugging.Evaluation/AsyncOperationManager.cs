// RuntimeInvokeManager.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Collections.Generic;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
	public class AsyncOperationManager: IDisposable
	{
		readonly List<AsyncOperation> operationsToCancel = new List<AsyncOperation> ();
		readonly object operationsSync = new object ();

		internal bool Disposing;

		public void Invoke (AsyncOperation methodCall, int timeout)
		{
			if (timeout <= 0)
				throw new ArgumentOutOfRangeException("timeout", timeout, "timeout must be greater than 0");
			methodCall.Aborted = false;
			methodCall.Manager = this;

			lock (operationsSync) {
				operationsToCancel.Add (methodCall);
				methodCall.BeginInvoke ();
			}

			if (timeout > 0) {
				if (!methodCall.WaitForCompleted (timeout)) {
					bool wasAborted = methodCall.Aborted;
					methodCall.InternalAbort ();
					lock (operationsSync) {
						operationsToCancel.Remove (methodCall);
					}
					if (wasAborted)
						throw new EvaluatorAbortedException ();
					else
						throw new TimeOutException ();
				}
			}

			lock (operationsSync) {
				operationsToCancel.Remove (methodCall);
				if (methodCall.Aborted) {
					throw new EvaluatorAbortedException ();
				}
			}

			if (!string.IsNullOrEmpty (methodCall.ExceptionMessage)) {
				throw new Exception (methodCall.ExceptionMessage);
			}
		}

		public void Dispose ()
		{
			Disposing = true;
			lock (operationsSync) {
				foreach (AsyncOperation op in operationsToCancel) {
					op.InternalShutdown ();
				}
				operationsToCancel.Clear ();
			}
		}

		public void AbortAll ()
		{
			lock (operationsSync) {
				foreach (AsyncOperation op in operationsToCancel)
					op.InternalAbort ();
			}
		}
		
		public void EnterBusyState (AsyncOperation oper)
		{
			if (BusyStateChanged != null)
				BusyStateChanged (this, new BusyStateEventArgs {
					IsBusy = true,
					Description = oper.Description
				});
		}
		
		public void LeaveBusyState (AsyncOperation oper)
		{
			if (BusyStateChanged != null)
				BusyStateChanged (this, new BusyStateEventArgs {
					IsBusy = false,
					Description = oper.Description
				});
		}
		
		public event EventHandler<BusyStateEventArgs> BusyStateChanged;
	}
}
