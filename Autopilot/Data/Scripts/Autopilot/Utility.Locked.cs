using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRage;

namespace Rynchodon
{
	public class Locked<T>
	{
		private T myValue;
		public FastResourceLock Lock = new	FastResourceLock();

		public Locked() { }

		public Locked(T initialValue)
		{ Value = initialValue; }

		/// <summary>
		/// safe access to Value
		/// </summary>
		public T Value
		{
			get
			{
				Lock.AcquireShared();
				try { return myValue; }
				finally { Lock.ReleaseShared(); }
			}
			set
			{
				Lock.AcquireExclusive();
				try { myValue = value; }
				finally { Lock.ReleaseExclusive(); }
			}
		}

		/// <summary>
		/// lock this object while performing safeAction
		/// </summary>
		/// <param name="safeAction"></param>
		public void Safe(Action safeAction)
		{
			Lock.AcquireExclusive();
			try { safeAction.Invoke(); }
			finally { Lock.ReleaseExclusive(); }
		}

		/// <summary>
		/// lock this object while performing safeAction
		/// </summary>
		/// <param name="safeAction"></param>
		public void SafeShared(Action safeAction)
		{
			Lock.AcquireShared();
			try { safeAction.Invoke(); }
			finally { Lock.ReleaseShared(); }
		}
	}
}
