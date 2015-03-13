using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using VRage;

namespace Rynchodon
{
	public class Locked<T> where T: struct
	{
		private T myValue;
		private FastResourceLock myLock = new	FastResourceLock();

		public Locked() { }

		public Locked(T initialValue)
		{ Value = initialValue; }

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
	}
}
