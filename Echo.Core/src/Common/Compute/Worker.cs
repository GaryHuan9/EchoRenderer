﻿using System;
using System.ComponentModel;
using System.Numerics;
using System.Threading;
using Echo.Core.Common.Diagnostics;

namespace Echo.Core.Common.Compute;

/// <summary>
/// An external delegation representing the <see cref="Worker"/> that can execute an <see cref="Operation"/>.
/// </summary>
public interface IWorker
{
	/// <summary>
	/// Two <see cref="IWorker"/> with the same <see cref="Index"/> will never execute the
	/// same <see cref="Operation"/> at the same time. Additionally, the value of this property
	/// will start at zero and continues on for different <see cref="IWorker"/>.
	/// </summary>
	int Index { get; }

	/// <summary>
	/// A globally unique identifier (<see cref="Guid"/>) for this <see cref="IWorker"/>.
	/// </summary>
	/// <remarks>The uniqueness of this value transcends different <see cref="Device"/>, while the
	/// uniqueness of <see cref="Index"/> does not; they are used for different purposes.</remarks>
	Guid Guid { get; }

	/// <summary>
	/// The current <see cref="WorkerState"/> of this <see cref="IWorker"/>.
	/// </summary>
	WorkerState State { get; }

	/// <summary>
	/// The <see cref="string"/> label of this <see cref="IWorker"/> to be displayed.
	/// </summary>
	sealed string DisplayLabel => $"Worker {Guid:D}";

	/// <summary>
	/// Invoked on this <see cref="IWorker"/> thread to block until a signal is issued.
	/// </summary>
	/// <param name="resetEvent">The <see cref="ManualResetEventSlim"/> used to set the signal.</param>
	void Await(ManualResetEventSlim resetEvent);

	/// <summary>
	/// Invoked on this <see cref="IWorker"/> thread to check if there are any scheduling changes.
	/// </summary>
	/// <remarks>Should be invoked periodically within the execution of an <see cref="Operation"/>.</remarks>
	void CheckSchedule();
}

/// <summary>
/// A <see cref="Thread"/> that works under a <see cref="Device"/> to jointly perform certain <see cref="Operation"/>s.
/// </summary>
sealed class Worker : IWorker, IDisposable
{
	public Worker(int index)
	{
		Index = index;
		Guid = Guid.NewGuid();
	}

	Operation nextOperation;
	Thread thread;

	readonly Locker locker = new();

	/// <inheritdoc/>
	public int Index { get; }

	/// <inheritdoc/>
	public Guid Guid { get; }

	uint _state = (uint)WorkerState.Unassigned;

	/// <inheritdoc/>
	public WorkerState State
	{
		get => (WorkerState)_state;
		private set
		{
			using var _ = locker.Fetch();

			WorkerState old = State;
			if (old == value) return;
			uint integer = (uint)value;

			Ensure.AreNotEqual(old, WorkerState.Disposed);
			Ensure.AreEqual(BitOperations.PopCount(integer), 1);

			Interlocked.Exchange(ref _state, integer);
			locker.Signal();
		}
	}

	volatile CancellationTokenSource abortTokenOwner = new();

	/// <summary>
	/// Invoked when this <see cref="IWorker"/> either begins or stops being dispatched to work on an <see cref="Operation"/>.
	/// </summary>
	/// <remarks>This is not invoked at the exact time when <see cref="State"/> changed, but rather when 
	/// the value is changed internally, thus it is more accurate but invoked on a different thread.</remarks>
	public event Action<IWorker, bool> OnDispatchChangedEvent;

	/// <summary>
	/// Invoked when this <see cref="IWorker"/> either begins or stops not using any computational resources. 
	/// </summary>
	/// <remarks>This is not invoked at the exact time when <see cref="State"/> changed, but rather when 
	/// the value is changed internally, thus it is more accurate but invoked on a different thread.</remarks>
	public event Action<IWorker, bool> OnIdlenessChangedEvent;

	/// <summary>
	/// Begins running an <see cref="Operation"/> on this idle <see cref="Worker"/>.
	/// </summary>
	/// <param name="operation">The <see cref="Operation"/> to dispatch</param>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="State"/> is not <see cref="WorkerState.Unassigned"/>.</exception>
	public void Dispatch(Operation operation)
	{
		using var _ = locker.Fetch();

		lock (locker)
		{
			if (State != WorkerState.Unassigned) throw new InvalidOperationException();

			//Assign operation
			Volatile.Write(ref nextOperation, operation);
			State = WorkerState.Running;
		}

		//Launch thread if needed
		if (thread == null)
		{
			thread = new Thread(Main)
			{
				IsBackground = true,
				Priority = ThreadPriority.Normal,
				Name = ((IWorker)this).DisplayLabel
			};

			thread.Start();
		}
	}

	/// <summary>
	/// If possible, pauses the <see cref="Operation"/> this <see cref="Worker"/> is currently performing as soon as possible.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="State"/> is <see cref="WorkerState.Disposed"/>.</exception>
	public void Pause()
	{
		using var _ = locker.Fetch();

		switch (State)
		{
			case WorkerState.Running:
			case WorkerState.Awaiting: break;
			case WorkerState.Unassigned:
			case WorkerState.Pausing:
			case WorkerState.Paused:
			case WorkerState.Aborting: return;
			case WorkerState.Disposed:
			default: throw new InvalidOperationException();
		}

		State = WorkerState.Pausing;
	}

	/// <summary>
	/// If possible, resumes the <see cref="Operation"/> this <see cref="Worker"/> was performing prior to pausing.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="State"/> is <see cref="WorkerState.Disposed"/>.</exception>
	public void Resume()
	{
		using var _ = locker.Fetch();

		switch (State)
		{
			case WorkerState.Pausing:
			case WorkerState.Paused: break;
			case WorkerState.Unassigned:
			case WorkerState.Running:
			case WorkerState.Awaiting:
			case WorkerState.Aborting: return;
			case WorkerState.Disposed:
			default: throw new InvalidOperationException();
		}

		State = WorkerState.Running;
	}

	/// <summary>
	/// If necessary, aborts the <see cref="Operation"/> this <see cref="Worker"/> is currently performing as soon as possible.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="State"/> is <see cref="WorkerState.Disposed"/>.</exception>
	public void Abort()
	{
		using var _ = locker.Fetch();

		switch (State)
		{
			case WorkerState.Running:
			case WorkerState.Pausing:
			case WorkerState.Paused:
			case WorkerState.Awaiting: break;
			case WorkerState.Unassigned:
			case WorkerState.Aborting: return;
			case WorkerState.Disposed:
			default: throw new InvalidOperationException();
		}

		abortTokenOwner.Cancel();
		State = WorkerState.Aborting;
	}

	public void Dispose()
	{
		lock (locker)
		{
			if (State == WorkerState.Disposed) return;

			Abort();
			AwaitAny(WorkerState.Unassigned);
			State = WorkerState.Disposed;

			locker.Signaling = false;
		}

		thread?.Join();
		abortTokenOwner.Dispose();
	}

	/// <inheritdoc/>
	void IWorker.Await(ManualResetEventSlim resetEvent)
	{
		Ensure.AreEqual(thread, Thread.CurrentThread);

		switch (State)
		{
			case WorkerState.Running:
			case WorkerState.Pausing:
			case WorkerState.Aborting: break;
			case WorkerState.Unassigned:
			case WorkerState.Paused:
			case WorkerState.Awaiting:
			case WorkerState.Disposed:
			default: throw new InvalidOperationException();
		}

		if (resetEvent.IsSet) return;

		var token = abortTokenOwner.Token;
		token.ThrowIfCancellationRequested();

		OnIdlenessChangedEvent?.Invoke(this, true);

		//Pause if requested
		while (true)
		{
			lock (locker)
			{
				if (token.IsCancellationRequested)
				{
					OnIdlenessChangedEvent?.Invoke(this, false);
					token.ThrowIfCancellationRequested();
				}

				WorkerState state = State;

				if (state == WorkerState.Running)
				{
					State = WorkerState.Awaiting;
					break;
				}

				if (state == WorkerState.Pausing) State = WorkerState.Paused;
				else throw new InvalidOperationException("Should be unreachable!"); //OPTIMIZE: switch to UnreachableException when upgraded to dotnet 7
			}

			AwaitAny(WorkerState.Running | WorkerState.Aborting);
		}

		//Begin awaiting for event
		try { resetEvent.Wait(token); }
		catch (OperationCanceledException)
		{
			OnIdlenessChangedEvent?.Invoke(this, false);
			Ensure.AreEqual(State, WorkerState.Aborting);
			throw;
		}

		//Change state after await finishes
		bool pause;

		lock (locker)
		{
			WorkerState state = State;

			pause = state == WorkerState.Pausing;
			if (pause) State = WorkerState.Paused;
			else if (state == WorkerState.Awaiting) State = WorkerState.Running;
		}

		//Optionally pause again if requested during await
		if (pause) AwaitAny(WorkerState.Running | WorkerState.Aborting);

		OnIdlenessChangedEvent?.Invoke(this, false);
		token.ThrowIfCancellationRequested();
	}

	/// <inheritdoc/>
	void IWorker.CheckSchedule()
	{
		Ensure.AreEqual(thread, Thread.CurrentThread);

		switch (State)
		{
			case WorkerState.Pausing:
			case WorkerState.Aborting: break;
			case WorkerState.Running: return;
			case WorkerState.Unassigned:
			case WorkerState.Paused:
			case WorkerState.Awaiting:
			case WorkerState.Disposed:
			default: throw new InvalidOperationException();
		}

		var token = abortTokenOwner.Token;
		token.ThrowIfCancellationRequested();

		lock (locker)
		{
			if (State != WorkerState.Pausing) return;
			State = WorkerState.Paused;
		}

		OnIdlenessChangedEvent?.Invoke(this, true);
		AwaitAny(WorkerState.Running | WorkerState.Aborting);
		OnIdlenessChangedEvent?.Invoke(this, false);

		token.ThrowIfCancellationRequested();
	}

	void Main()
	{
		while (AwaitAny(WorkerState.Running) != WorkerState.Disposed)
		{
			Operation operation = Interlocked.Exchange(ref nextOperation, null);
			if (operation == null) throw new InvalidAsynchronousStateException();

			OnDispatchChangedEvent?.Invoke(this, true);
			bool running;

			do
			{
				try
				{
					running = operation.Execute(this);
					((IWorker)this).CheckSchedule();
				}
				catch (OperationCanceledException) { break; }
			}
			while (running);

			lock (locker)
			{
				State = WorkerState.Unassigned;

				if (!abortTokenOwner.TryReset())
				{
					//Reset abort cancellation token
					CancellationTokenSource old = abortTokenOwner;
					abortTokenOwner = new CancellationTokenSource();
					old.Dispose();
				}
			}

			OnDispatchChangedEvent?.Invoke(this, false);
		}
	}

	WorkerState AwaitAny(WorkerState states)
	{
		states |= WorkerState.Disposed;

		lock (locker)
		{
			WorkerState current = State;

			while ((current | states) != states)
			{
				locker.Wait();
				current = State;
			}

			return current;
		}
	}
}