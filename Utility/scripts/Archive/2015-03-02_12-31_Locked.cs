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
		private FastResourceLock myLock = new	FastResourceLock();

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
				myLock.AcquireExclusive();
				try { return myValue; }
				finally { myLock.ReleaseExclusive(); }
			}
			set
			{
				myLock.AcquireExclusive();
				try { myValue = value; }
				finally { myLock.ReleaseExclusive(); }
			}
		}

		/// <summary>
		/// lock this object while performing safeAction
		/// </summary>
		/// <param name="safeAction"></param>
		public void Safe(Action safeAction)
		{
			myLock.AcquireExclusive();
			try { safeAction.Invoke(); }
			finally { myLock.ReleaseExclusive(); }
		}
	}
}
