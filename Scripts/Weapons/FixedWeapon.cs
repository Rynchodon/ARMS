using System.Collections.Generic;
using Rynchodon.Utility;
using Rynchodon.Utility.Network;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Weapons
{
	/// <summary>
	/// For rotor-turrets and Autopilot-usable weapons.
	/// </summary>
	public sealed class FixedWeapon : WeaponTargeting
	{

		static FixedWeapon()
		{
			MessageHandler.AddHandler(MessageHandler.SubMod.FW_EngagerControl, Handler_EngagerControl);
		}

		/// <summary>
		/// Server sends message to clients to let them know the weapon is being controlled by engager.
		/// </summary>
		private static void SendToClient_EngagerControl(long entityId, bool control)
		{
			if (MyAPIGateway.Multiplayer.IsServer)
				return;

			List<byte> message = new List<byte>();

			ByteConverter.AppendBytes(message, (byte)MessageHandler.SubMod.FW_EngagerControl);
			ByteConverter.AppendBytes(message, entityId);
			ByteConverter.AppendBytes(message, control);

			if (!MyAPIGateway.Multiplayer.SendMessageToOthers(MessageHandler.ModId, message.ToArray()))
				Logger.AlwaysLog("Failed to send message", Logger.severity.ERROR);
		}

		private static void Handler_EngagerControl(byte[] message, int pos)
		{
			long entityId = ByteConverter.GetLong(message, ref pos);
			bool control  = ByteConverter.GetBool(message, ref pos);

			WeaponTargeting weapon;
			if (!Registrar.TryGetValue(entityId, out weapon))
			{
				Logger.DebugLog("Weapon not in registrar: " + entityId, Logger.severity.WARNING);
				return;
			}

			if (control)
				((FixedWeapon)weapon).EngagerTakeControl();
			else
				((FixedWeapon)weapon).EngagerReleaseControl();
		}

		private readonly bool AllowFighterControl;

		private MotorTurret MyMotorTurret = null;

		private Logable Log { get { return new Logable(CubeBlock); } }

		public FixedWeapon(IMyCubeBlock block)
			: base(block)
		{
			//Registrar.Add(CubeBlock, this);
			//Log.DebugLog("Initialized", "FixedWeapon()");

			AllowFighterControl = WeaponDescription.GetFor(block).AllowFighterControl;
		}

		/// <summary>
		/// Attempts to take control of this fixed weapon, it must not be a rotor-turret.
		/// </summary>
		/// <returns>true iff the engager can control this weapon</returns>
		public bool EngagerTakeControl()
		{
			if (AllowFighterControl && CanControl && MyMotorTurret == null)
			{
				//Log.DebugLog("engager takes control", "EngagerTakeControl()");
				CurrentControl = Control.Engager;

				if (MyAPIGateway.Multiplayer.IsServer)
					SendToClient_EngagerControl(CubeBlock.EntityId, true);
				return true;
			}

			return false;
		}

		public void EngagerReleaseControl()
		{
			CurrentControl = Control.Off;
			if (MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.IsServer)
				SendToClient_EngagerControl(CubeBlock.OwnerId, false);
		}

		public IMyCubeGrid MotorTurretBaseGrid()
		{
			return MyMotorTurret == null || MyMotorTurret.StatorAz == null || MyMotorTurret.StatorAz.Closed ? null : (IMyCubeGrid)MyMotorTurret.StatorAz.CubeGrid;
		}

		public IMyCubeBlock MotorTurretFaceBlock()
		{
			return MyMotorTurret == null || MyMotorTurret.StatorAz == null || MyMotorTurret.StatorAz.Closed ? null : (IMyCubeBlock)MyMotorTurret.StatorAz;
		}

		/// <summary>
		/// Fires the weapon as it bears.
		/// </summary>
		protected override void Update1_GameThread()
		{
			if (CurrentControl == Control.Off || !MyAPIGateway.Multiplayer.IsServer)
				return;

			// CurrentTarget may be changed by WeaponTargeting
			Target GotTarget = CurrentTarget;
			if (GotTarget.Entity == null)
			{
				//FireWeapon = false;
				if (MyMotorTurret != null)
					MyMotorTurret.Stop();
				return;
			}
			if (!GotTarget.FiringDirection.HasValue || !GotTarget.ContactPoint.HasValue) // happens alot
				return;

			if (MyMotorTurret != null)
				MyMotorTurret.FaceTowards(GotTarget.FiringDirection.Value);
		}

		protected override void Update100_Options_TargetingThread(TargetingOptions current)
		{
			if (CurrentControl == Control.Engager)
				return;

			//Log.DebugLog("Turret flag: " + current.FlagSet(TargetingFlags.Turret) + ", No motor turret: " + (MyMotorTurret == null) + ", CanControl = " + CanControl, "Update_Options()");
			if (current.FlagSet(TargetingFlags.Turret))
			{
				if (MyMotorTurret == null && CanControl)
				{
					//Log.DebugLog("MotorTurret is now enabled", "Update_Options()", Logger.severity.INFO);
					MyMotorTurret = new MotorTurret(CubeBlock, MyMotorTurret_OnStatorChange, 1, WeaponDefinition.RequiredAccuracyRadians);
				}
			}
			else
			{
				if (MyMotorTurret != null)
				{
					//Log.DebugLog("MotorTurret is now disabled", "Update_Options()", Logger.severity.INFO);
					MyMotorTurret.Dispose();
					MyMotorTurret = null; // MyMotorTurret will not be updated, so it will be recreated later incase something weird happens to motors
				}
			}
		}

		protected override bool CanRotateTo(ref Vector3D targetPoint, IMyEntity target)
		{
			if (MyMotorTurret == null)
				return true;

			Vector3 direction = targetPoint - ProjectilePosition();
			direction.Normalize();
			return MyMotorTurret.CanFaceTowards(direction);
		}

		public override Vector3 Facing()
		{
			return CubeBlock.WorldMatrix.Forward;
		}

		/// <summary>
		/// Creates an obstruction list from stators and rotors.
		/// </summary>
		private void MyMotorTurret_OnStatorChange(IMyMotorStator statorEl, IMyMotorStator statorAz)
		{
			//Log.DebugLog("entered MyMotorTurret_OnStatorChange()", "MyMotorTurret_OnStatorChange()");

			List<IMyEntity> ignore = new List<IMyEntity>();
			if (statorEl != null)
			{
				//Log.DebugLog("added statorEl.CubeGrid: " + statorEl.CubeGrid.getBestName(), "MyMotorTurret_OnStatorChange()");
				ignore.Add(statorEl.CubeGrid);

				// elevation rotor will be on same grid as weapon, already ignored
			}
			if (statorAz != null)
			{
				//Log.DebugLog("added statorAz: " + statorAz.getBestName(), "MyMotorTurret_OnStatorChange()");
				ignore.Add(statorAz);

				// azimuth rotor will be on same grid as elevation stator, ignored above
			}
			Ignore(ignore);
		}

	}
}
