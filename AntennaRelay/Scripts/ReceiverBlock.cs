using System.Collections.Generic;
using Rynchodon.Attached;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.AntennaRelay
{
	public abstract class ReceiverBlock : Receiver
	{

		/// <summary>
		/// For sending newly created LastSeen to all attached antennae and ShipController.
		/// </summary>
		/// <param name="sender">The block doing the sending.</param>
		/// <param name="toSend">The LastSeen to send.</param>
		public static void SendToAttached(IMyCubeBlock sender, ICollection<LastSeen> toSend)
		{
			Registrar.ForEach((RadioAntenna radioAnt) => {
				if (sender.canSendTo(radioAnt.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						radioAnt.Receive(seen);
			});

			Registrar.ForEach((LaserAntenna laserAnt) => {
				if (sender.canSendTo(laserAnt.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						laserAnt.Receive(seen);
			});

			Registrar.ForEach((ShipController controller) => {
				if (sender.canSendTo(controller.CubeBlock, true))
					foreach (LastSeen seen in toSend)
						controller.Receive(seen);
			});
		}

		/// <summary>
		/// For sending newly created Message to all attached antennae.
		/// </summary>
		/// <param name="sender">The block doing the sending.</param>
		/// <param name="toSend">The Message to send.</param>
		public static void SendToAttached(IMyCubeBlock sender, ICollection<Message> toSend)
		{
			Registrar.ForEach((RadioAntenna radioAnt) => {
				if (sender.canSendTo(radioAnt.CubeBlock, true))
					foreach (Message msg in toSend)
						radioAnt.Receive(msg);
			});

			Registrar.ForEach((LaserAntenna laserAnt) => {
				if (sender.canSendTo(laserAnt.CubeBlock, true))
					foreach (Message msg in toSend)
						laserAnt.Receive(msg);
			});
		}

		private readonly Logger myLogger = new Logger(null, "Receiver");

		public IMyCubeBlock CubeBlock { get; private set; }

		protected ReceiverBlock(IMyCubeBlock block)
			: base(block.CubeGrid)
		{
			this.CubeBlock = block;
			CubeBlock.CubeGrid.OnBlockOwnershipChanged += CubeGrid_OnBlockOwnershipChanged;

			myLogger = new Logger("ReceiverBlock", () => CubeBlock.CubeGrid.DisplayName);

			CubeBlock.OnClosing += OnClosing;
		}

		private void OnClosing(IMyEntity entity)
		{
			CubeBlock.CubeGrid.OnBlockOwnershipChanged -= CubeGrid_OnBlockOwnershipChanged;
			CubeBlock = null;
		}

		private void CubeGrid_OnBlockOwnershipChanged(IMyCubeGrid obj)
		{
			if (obj == CubeBlock)
				ClearMessages();
		}

		/// <summary>
		/// Copies all LastSeen to all attached Receiver.
		/// If attached to the destination of a message, sends the message to the destination. Otherwise, sends it to all attached antennae.
		/// Removes invalid LastSeen and Message
		/// </summary>
		protected void RelayAttached()
		{
			ForEachLastSeen(seen => {
				Registrar.ForEach((RadioAntenna radioAnt) => {
					if (CubeBlock.canSendTo(radioAnt.CubeBlock, true))
						radioAnt.Receive(seen);
				});

				Registrar.ForEach((LaserAntenna laserAnt) => {
					if (CubeBlock.canSendTo(laserAnt.CubeBlock, true))
						laserAnt.Receive(seen);
				});

				Registrar.ForEach((ShipController remote) => {
					if (CubeBlock.canSendTo(remote.CubeBlock, true))
						remote.Receive(seen);
				});

				return false;
			});

			ForEachMessage(msg => {
				if (AttachedGrid.IsGridAttached(CubeBlock.CubeGrid, msg.DestCubeBlock.CubeGrid, AttachedGrid.AttachmentKind.Terminal))
				{
					// get receiver for block
					ShipController remote;
					if (Registrar.TryGetValue(msg.DestCubeBlock.EntityId, out remote))
					{
						remote.Receive(msg);
						msg.IsValid = false;
						return false;
					}
					ProgrammableBlock progBlock;
					if (Registrar.TryGetValue(msg.DestCubeBlock.EntityId, out progBlock))
					{
						progBlock.Receive(msg);
						msg.IsValid = false;
						return false;
					}

					myLogger.alwaysLog("Message has invalid destination: " + msg.ToString(), "RelayAttached()", Logger.severity.WARNING);
					msg.IsValid = false;
					return false;
				}

				// send to all radio antenna
				Registrar.ForEach((RadioAntenna radioAnt) => {
					if (CubeBlock.canSendTo(radioAnt.CubeBlock, true))
						radioAnt.Receive(msg);
				});

				Registrar.ForEach((LaserAntenna laserAnt) => {
					if (CubeBlock.canSendTo(laserAnt.CubeBlock, true))
						laserAnt.Receive(msg);
				});

				return false;
			});
		}

	}

	public static class ReceiverExtensions
	{
		public static bool IsOpen(this ReceiverBlock receiver)
		{ return receiver != null && receiver.CubeBlock != null && !receiver.CubeBlock.Closed; }
	}
}
