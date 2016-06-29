using System;
using System.Collections.Generic;
using VRage;

namespace Rynchodon
{
	/// <summary>
	/// Acquires a shared lock while an enumerator is running.
	/// </summary>
	/// <typeparam name="T">Type of object yielded by enumerator.</typeparam>
	public class LockedEnumerator<T> : IEnumerator<T>
	{

		private FastResourceLock m_fastLock;
		private IEnumerator<T> m_enumerator;

		// delay grabbing enumerator in case collection is modified while waiting to acquire lock
		public LockedEnumerator(Func<IEnumerator<T>> enumerator, FastResourceLock fastLock)
		{
			fastLock.AcquireSharedUsing();
			m_fastLock = fastLock;
			m_enumerator = enumerator.Invoke();
		}

		~LockedEnumerator()
		{
			Dispose();
		}

		#region IEnumerator<T> Members

		public T Current
		{
			get { return m_enumerator.Current; }
		}

		#endregion

		#region IDisposable Members

		public void Dispose()
		{
			if (m_fastLock == null)
				return;
			FastResourceLock fastLock = m_fastLock;
			m_fastLock = null;
			m_enumerator.Dispose();
			m_enumerator = null;
			fastLock.ReleaseShared();
		}

		#endregion

		#region IEnumerator Members

		object System.Collections.IEnumerator.Current
		{
			get { return Current; }
		}

		public bool MoveNext()
		{
			return m_enumerator.MoveNext();
		}

		public void Reset()
		{
			m_enumerator.Reset();
		}

		#endregion

		public IEnumerator<T> GetEnumerator()
		{
			return this;
		}
	}
}
