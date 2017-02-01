#if DEBUG
#define TRACE
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using VRageMath;

namespace Rynchodon.Utility.Network
{
	public static class TerminalSyncMessage
	{

		private static Dictionary<Type, IEqualityComparer> _equalityComparers;

		static TerminalSyncMessage()
		{
			_equalityComparers = new Dictionary<Type, IEqualityComparer>();
			_equalityComparers.Add(typeof(StringBuilder), new EqualityComparer_StringBuilder());
			_equalityComparers.Add(typeof(Vector3D), new EqualityComparer_Vector3D());
		}

		public static void AddSyncMessage(this List<TerminalSync.SyncMessage> list, ulong? recipient, TerminalSync.Id id, object value, long blockId)
		{
			if (list == null)
				throw new ArgumentNullException("list");

			Logger.TraceLog("recipient: " + recipient + ", id: " + id + ", value: " + value + ", blockId: " + blockId);
			foreach (TerminalSync.SyncMessage existing in list)
				if (RecipientEquals(existing.recipient, recipient) && existing.id == id && ValueEquals(existing.value, value))
				{
					Logger.TraceLog("recipient, id, and value match existing SyncMessage, appending blockId");
					existing.blockId.Add(blockId);
					return;
				}

			Logger.TraceLog("creating new SyncMessage");
			list.Add(new TerminalSync.SyncMessage(recipient, id, value, blockId));
		}

		public static void AddSyncMessage(this List<TerminalSync.SyncMessage> list, TerminalSync.SyncMessage newValue)
		{
			if (list == null)
				throw new ArgumentNullException("list");

			Logger.TraceLog("newValue " + newValue);
			foreach (TerminalSync.SyncMessage existing in list)
				if (RecipientEquals(existing.recipient, newValue.recipient) && existing.id == newValue.id && ValueEquals(existing.value, newValue.value))
				{
					Logger.TraceLog("recipient, id, and value match existing SyncMessage, appending blockId");
					existing.blockId.AddList(newValue.blockId);
					return;
				}

			Logger.TraceLog("adding newValue");
			list.Add(newValue);
		}

		private static bool RecipientEquals(ulong? first, ulong? second)
		{
			if (!first.HasValue)
				return !second.HasValue;
			if (!second.HasValue)
				return false;

			return first.Value == second.Value;
		}

		private static bool ValueEquals(object first, object second)
		{
			if (first == null)
				return second == null;
			if (second == null)
				return false;

			Type type = first.GetType();
			if (type != second.GetType())
				return false;

			IEqualityComparer comparer;
			if (_equalityComparers.TryGetValue(type, out comparer))
				return comparer.Equals(first, second);

			return first.Equals(second);
		}

	}
}
