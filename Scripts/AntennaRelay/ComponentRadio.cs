using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.AntennaRelay
{
	/// <summary>
	/// Radio component of a NetworkNode
	/// </summary>
	public abstract class ComponentRadio
	{

		/// <summary>
		/// Tries to create a radio component for an entity. Creates the radio component if the entity is a radio antenna block, beacon block, or a character.
		/// </summary>
		/// <param name="obj">Entity to create the component for.</param>
		/// <returns>A new radio component for the entity, if it can be created. Null, otherwise.</returns>
		public static ComponentRadio TryCreateRadio(IMyEntity obj)
		{
			IMyRadioAntenna radioAnt = obj as IMyRadioAntenna;
			if (radioAnt != null)
				return new CR_AntennaBlock(radioAnt);

			IMyBeacon beacon = obj as IMyBeacon;
			if (beacon != null)
				return new CR_BeaconBlock(beacon);

			IMyCharacter character = obj as IMyCharacter;
			if (character != null)
				return new CR_Character(character);

			return null;
		}

		/// <summary>
		/// Creates a radio component for a character.
		/// </summary>
		public static ComponentRadio CreateRadio(IMyCharacter character)
		{
			return new CR_Character(character);
		}

		/// <summary>
		/// Creates a radio component for an entity.
		/// </summary>
		public static ComponentRadio CreateRadio(IMyEntity entity, float radius = 0f)
		{
			return new CR_SimpleAntenna(entity, radius);
		}

		/// <summary>The entity that has the radio.</summary>
		public abstract IMyEntity Entity { get; }
		/// <summary>Radio is functioning.</summary>
		public abstract bool IsWorking { get; }
		/// <summary>Radio is capable of receiving transmissions.</summary>
		public abstract bool CanReceive { get; }
		/// <summary>Radio is capable of broadcasting data and broadcasting is enabled.</summary>
		public abstract bool CanBroadcastData { get; }
		/// <summary>Radio is capable of broacasting its position.</summary>
		public abstract bool CanBroadcastPosition { get; }
		/// <summary>Broadcast radius (if broadcasting).</summary>
		public abstract float Radius { get; }

		/// <summary>
		/// Tests for a connection from this radio to another radio.
		/// </summary>
		/// <param name="other">The radio that may be connected.</param>
		public RelayNode.CommunicationType TestConnection(ComponentRadio other)
		{
			if (!IsWorking || !other.IsWorking)
				return RelayNode.CommunicationType.None;

			if (!CanBroadcastData || !other.CanReceive)
				return RelayNode.CommunicationType.None;

			float distSquared = Vector3.DistanceSquared(Entity.GetPosition(), other.Entity.GetPosition());
			if (distSquared > Radius * Radius)
				return RelayNode.CommunicationType.None;

			if (!CanReceive || !other.CanBroadcastData)
				return RelayNode.CommunicationType.OneWay;

			if (distSquared > other.Radius * other.Radius)
				return RelayNode.CommunicationType.OneWay;

			return RelayNode.CommunicationType.TwoWay;
		}

		/// <summary>
		/// Tests if this radio is detected by another radio.
		/// </summary>
		/// <param name="other">The radio that may detect this one.</param>
		/// <returns>True iff the other radio detects this one.</returns>
		public bool CanBroadcastPositionTo(ComponentRadio other)
		{
			if (!IsWorking || !other.IsWorking)
				return false;

			if (!CanBroadcastPosition || !other.CanReceive)
				return false;

			float distSquared = Vector3.DistanceSquared(Entity.GetPosition(), other.Entity.GetPosition());
			return distSquared <= Radius * Radius;
		}

		private class CR_AntennaBlock : ComponentRadio
		{

			private static ITerminalProperty<bool> s_prop_broadcasting;

			private IMyRadioAntenna m_antenna;

			public override IMyEntity Entity
			{
				get { return m_antenna; }
			}

			public override bool IsWorking
			{
				get { return m_antenna.IsWorking; }
			}

			public override bool CanReceive
			{
				get { return true; }
			}

			public override bool CanBroadcastData
			{
				get { return s_prop_broadcasting.GetValue(m_antenna); }
			}

			public override bool CanBroadcastPosition
			{
				get { return CanBroadcastData; }
			}

			public override float Radius
			{
				get { return m_antenna.Radius; }
			}

			public CR_AntennaBlock(IMyRadioAntenna antenna)
			{
				this.m_antenna = antenna;

				if (s_prop_broadcasting == null)
					s_prop_broadcasting = antenna.GetProperty("EnableBroadCast").AsBool();
			}

		}

		private class CR_BeaconBlock : ComponentRadio
		{

			private IMyBeacon m_beacon;

			public override IMyEntity Entity
			{
				get { return m_beacon; }
			}

			public override bool IsWorking
			{
				get { return m_beacon.IsWorking; }
			}

			public override bool CanReceive
			{
				get { return false; }
			}

			public override bool CanBroadcastData
			{
				get { return false; }
			}

			public override bool CanBroadcastPosition
			{
				get { return true; }
			}

			public override float Radius
			{
				get { return m_beacon.Radius; }
			}

			public CR_BeaconBlock(IMyBeacon beacon)
			{
				this.m_beacon = beacon;
			}

		}

		private class CR_Character : ComponentRadio
		{

			private IMyCharacter m_character;
			private IMyIdentity m_identity;

			public override IMyEntity Entity
			{
				get { return m_character as IMyEntity; }
			}

			public override bool IsWorking
			{
				get { return !m_identity.IsDead; }
			}

			public override bool CanReceive
			{
				get { return true; }
			}

			public override bool CanBroadcastData
			{
				get { return (m_character as Sandbox.Game.Entities.IMyControllableEntity).EnabledBroadcasting; }
			}

			public override bool CanBroadcastPosition
			{
				get { return CanBroadcastData; }
			}

			public override float Radius
			{
				get { return Globals.PlayerBroadcastRadius; }
			}

			public CR_Character(IMyCharacter character)
			{
				this.m_character = character;
				this.m_identity = character.GetIdentity_Safe();
			}

		}

		private class CR_SimpleAntenna : ComponentRadio
		{

			private IMyEntity m_entity;
			private float m_radius = 0;

			public override IMyEntity Entity
			{
				get { return m_entity; }
			}

			public override bool IsWorking
			{
				get { return true; }
			}

			public override bool CanReceive
			{
				get { return true; }
			}

			public override bool CanBroadcastData
			{
				get { return m_radius >= 1f; }
			}

			public override bool CanBroadcastPosition
			{
				get { return CanBroadcastData; }
			}

			public override float Radius
			{
				get { return m_radius; }
			}

			public CR_SimpleAntenna(IMyEntity entity, float radius = 0f)
			{
				this.m_entity = entity;
				this.m_radius = radius;
			}

		}

	}
}
