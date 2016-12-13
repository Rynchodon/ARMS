using System;

namespace Rynchodon.Utility.Network
{
	class MessageTooLongException : Exception
	{
		public MessageTooLongException(int length) : base("Length: " + length) { }
	}
}
